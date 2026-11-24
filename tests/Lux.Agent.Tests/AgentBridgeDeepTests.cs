using Lux.Agent;
using Lux.Dashboard;
using Lux.Form;
using Lux.Spreadsheet;
using Xunit;

namespace Lux.Agent.Tests;

public sealed class AgentBridgeDeepTests
{
    [Fact]
    public async Task AgentBridge_supports_Form_toggle_clear_reset_and_stepped_wizard_navigation()
    {
        var agent = new AgentBridgeService();
        var wizard = new FormController(new FormSchema
        {
            FormKey = "onboarding",
            Title = "Onboarding Wizard",
            Steps =
            [
                new FormStepSchema
                {
                    Key = "step1", Title = "Account",
                    Fields =
                    [
                        new FormFieldSchema { Key = "org", Label = "Org Name", DefaultValue = "InitialOrg", Required = true },
                        new FormFieldSchema { Key = "newsletter", Label = "Newsletter", Kind = FormFieldKind.Checkbox, DefaultValue = false },
                        new FormFieldSchema { Key = "hiddenNote", Label = "Hidden Note", Condition = f => f.GetValue<bool>("newsletter") }
                    ]
                },
                new FormStepSchema
                {
                    Key = "step2", Title = "Confirm",
                    Fields = [new FormFieldSchema { Key = "accepted", Label = "Accepted", Kind = FormFieldKind.Checkbox, DefaultValue = true }]
                }
            ]
        });

        agent.RegisterForm(wizard);

        // Off-page / conditional field excluded when includeOffPage=false
        var visibleOnly = agent.Snapshot(includeOffPage: false);
        Assert.DoesNotContain(visibleOnly, s => s.Label == "Hidden Note");
        Assert.Contains(agent.Snapshot(includeOffPage: true), s => s.Label == "Hidden Note");

        // Toggle checkbox -> Hidden Note becomes visible; clear & reset org; navigate steps
        var act1 = await agent.ActAsync([
            new AgentActStep("Newsletter", "toggle"),
            new AgentActStep("Org Name", "setValue", ["LuxCorp"]),
            new AgentActStep("Onboarding Wizard", "nextStep")
        ]);
        Assert.True(act1.Ok);
        Assert.True(wizard.GetValue<bool>("newsletter"));
        Assert.Equal("LuxCorp", wizard.GetValue<string>("org"));
        Assert.Equal("step2", wizard.CurrentStep?.Key);

        var act2 = await agent.ActAsync([
            new AgentActStep("Onboarding Wizard", "previousStep"),
            new AgentActStep("Org Name", "reset")
        ]);
        Assert.True(act2.Ok);
        Assert.Equal("step1", wizard.CurrentStep?.Key);
        Assert.Equal("InitialOrg", wizard.GetValue<string>("org"));

        var act3 = await agent.ActAsync([new AgentActStep("Org Name", "clear")]);
        Assert.True(act3.Ok);
        Assert.Null(wizard.GetValue("org"));
    }

    [Fact]
    public async Task AgentBridge_controls_Dashboard_filters_reset_and_refresh()
    {
        var refreshes = 0;
        var dash = new DashboardController("revenue");
        dash.AddView("overview", "Overview");
        dash.AddView("cohorts", "Cohorts");
        var region = dash.AddFilter(new DashboardFilterControl(
            "region", "Region",
            items: [new("eu", "Europe"), new("us", "United States")],
            defaultValues: ["eu"],
            multiple: true));
        dash.RegisterResource(new DashboardResource<int>(
            "kpi", _ => Task.FromResult(++refreshes), DashboardQueryStage.Essential));

        var agent = new AgentBridgeService();
        agent.RegisterDashboard(dash);

        var q = agent.Query(new AgentQueryRequest { Within = "Region", Text = "united" });
        Assert.Single(q);
        Assert.Equal("us", q[0].Items[0].Value);

        var act = await agent.ActAsync([
            new AgentActStep("Region", "toggle", ["us"]),
            new AgentActStep("revenue", "setView", ["cohorts"]),
            new AgentActStep("revenue", "refresh")
        ]);
        Assert.True(act.Ok);
        Assert.Equal(["eu", "us"], region.Values);
        Assert.Equal("cohorts", dash.CurrentView);
        Assert.Equal(1, refreshes);

        var reset = await agent.ActAsync([new AgentActStep("Region", "reset")]);
        Assert.True(reset.Ok);
        Assert.Equal(["eu"], region.Values);
    }

    [Fact]
    public async Task AgentBridge_drives_SpreadsheetImporter_steps_and_submit()
    {
        var imported = 0;
        var importer = new SpreadsheetImporter(new SpreadsheetSchema
        {
            Key = "contacts-import",
            Columns = [new SpreadsheetColumnSchema { Key = "name", Label = "Name", Required = true }],
            OnSubmit = rows =>
            {
                imported += rows.Count;
                return Task.FromResult<IReadOnlyList<SpreadsheetRejectedRow>?>([]);
            }
        });
        importer.LoadText("Name\nAlice\nBob", "contacts.csv");

        var agent = new AgentBridgeService();
        agent.RegisterSpreadsheet(importer);

        var snap = agent.Snapshot();
        var sheetSnap = snap.First(s => s.Component == "spreadsheet");
        Assert.Contains("nextStep", sheetSnap.Actions);
        Assert.Contains("submit", sheetSnap.Actions);

        var act = await agent.ActAsync([
            new AgentActStep(sheetSnap.Ref, "nextStep"),
            new AgentActStep(sheetSnap.Ref, "submit")
        ]);
        Assert.True(act.Ok);
        Assert.Equal(2, imported);
    }

    [Fact]
    public async Task AgentBridge_halts_multi_step_ActAsync_on_error_respects_AllowPolicy_and_disposes_Observe()
    {
        var agent = new AgentBridgeService();
        var form = new FormController(new FormSchema
        {
            FormKey = "cfg",
            Title = "Config",
            Fields = [new FormFieldSchema { Key = "a", Label = "Field A", DefaultValue = "1" }, new FormFieldSchema { Key = "b", Label = "Field B", DefaultValue = "2" }]
        });
        agent.RegisterForm(form, confirmSubmit: true);

        var events = new List<ObservedChange>();
        var sub = agent.Observe(events.Add);

        // Failing step 2 halts before step 3 executes
        var fail = await agent.ActAsync([
            new AgentActStep("Field A", "setValue", ["updated-a"]),
            new AgentActStep("DoesNotExist", "setValue", ["x"]),
            new AgentActStep("Field B", "setValue", ["should-not-run"])
        ]);
        Assert.False(fail.Ok);
        Assert.Equal("updated-a", form.GetValue<string>("a"));
        Assert.Equal("2", form.GetValue<string>("b"));
        Assert.Single(events);

        // AllowPolicy approving confirmed submit succeeds
        agent.AllowPolicy = (_, _) => Task.FromResult(true);
        var approved = await agent.ActAsync([new AgentActStep("Config", "submit")]);
        Assert.True(approved.Ok);

        // Dispose observer -> no further events delivered
        var countBefore = events.Count;
        sub.Dispose();
        await agent.ActAsync([new AgentActStep("Field B", "setValue", ["after-dispose"])]);
        Assert.Equal(countBefore, events.Count);
    }

    [Fact]
    public void UndoStack_edge_cases_empty_undo_redo_and_limit_one()
    {
        var stack = new UndoStack<int>(10, limit: 1);
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal(10, stack.Undo());
        Assert.Equal(10, stack.Redo());

        stack.Record(20);
        stack.Record(30);
        Assert.True(stack.CanUndo);
        Assert.Equal(20, stack.Undo());
        Assert.False(stack.CanUndo); // Limit 1 dropped 10
        Assert.True(stack.CanRedo);
        Assert.Equal(30, stack.Redo());
        Assert.False(stack.CanRedo);
    }
}
