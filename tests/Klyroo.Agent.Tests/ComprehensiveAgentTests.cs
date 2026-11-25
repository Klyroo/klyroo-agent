using Klyroo.Agent;
using Klyroo.Dashboard;
using Klyroo.Form;
using Klyroo.Spreadsheet;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class ComprehensiveAgentTests
{
    [Fact]
    public async Task AgentBridge_redacts_secrets_queries_accent_insensitively_and_diagnoses_all_errors()
    {
        var agent = new AgentBridgeService();
        var observed = new List<ObservedChange>();
        using var sub = agent.Observe(observed.Add);

        var form = new FormController(new FormSchema
        {
            FormKey = "cfg",
            Title = "Project Settings",
            Fields =
            [
                new FormFieldSchema { Key = "name", Label = "Project Name", DefaultValue = "Alpha" },
                new FormFieldSchema { Key = "secret", Label = "API Secret", Kind = FormFieldKind.Password, DefaultValue = "super-secret" },
                new FormFieldSchema { Key = "region", Label = "Région", Kind = FormFieldKind.Select, DefaultValue = "eu-west", Options = [new("eu-west", "Europe West"), new("us-east", "US East")] },
                new FormFieldSchema { Key = "locked", Label = "Locked Field", DisabledWhen = _ => true }
            ]
        });

        var dash = new DashboardController("rev");
        dash.AddView("overview", "Overview");
        dash.AddView("cohorts", "Cohorts");

        agent.RegisterForm(form, confirmSubmit: true);
        agent.RegisterDashboard(dash);

        // 1. Secret redaction
        var snap = agent.Snapshot();
        Assert.Equal(AgentBridgeService.RedactedValue, snap.First(s => s.Label == "API Secret").Value);

        // 2. Accent-insensitive Query without opening popups
        var q = agent.Query(new AgentQueryRequest { Within = "region", Text = "Europe" });
        Assert.Equal("eu-west", q[0].Items[0].Value);

        // 3. Multi-controller ActAsync
        var ok = await agent.ActAsync([
            new AgentActStep("Project Name", "setValue", ["Klyroo"]),
            new AgentActStep("rev", "setView", ["cohorts"])
        ]);
        Assert.True(ok.Ok);
        Assert.Equal("Klyroo", form.GetValue<string>("name"));
        Assert.Equal("cohorts", dash.CurrentView);
        Assert.Contains(observed, o => o.Source == AgentChangeSource.Agent && o.Label == "Project Name");

        // 4. Deterministic failure diagnostics
        var notFound = await agent.ActAsync([new AgentActStep("missing", "setValue", ["x"])]);
        Assert.False(notFound.Ok);
        Assert.Contains("not found", notFound.Error);

        var disabled = await agent.ActAsync([new AgentActStep("Locked Field", "setValue", ["x"])]);
        Assert.False(disabled.Ok);
        Assert.Contains("disabled", disabled.Error);

        var unsupported = await agent.ActAsync([new AgentActStep("Project Name", "submit")]);
        Assert.False(unsupported.Ok);
        Assert.Contains("not supported", unsupported.Error);

        agent.AllowPolicy = (_, confirmRequired) => Task.FromResult(!confirmRequired);
        var refused = await agent.ActAsync([new AgentActStep("Project Settings", "submit")]);
        Assert.False(refused.Ok);
        Assert.Contains("refused by policy", refused.Error);

        Assert.Equal(3, AgentBridgeService.GetToolDefinitions().Count);
    }

    [Fact]
    public void UndoStack_enforces_limit_truncates_redo_branch_and_clears()
    {
        var stack = new UndoStack<string>("v0", limit: 2);
        stack.Record("v1");
        stack.Record("v2");
        stack.Record("v3");

        Assert.Equal("v3", stack.Current);
        Assert.Equal("v2", stack.Undo());
        Assert.Equal("v1", stack.Undo());
        Assert.False(stack.CanUndo); // limit = 2 dropped v0

        Assert.Equal("v2", stack.Redo());
        stack.Record("v2-branch");
        Assert.False(stack.CanRedo); // redo branch truncated

        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.Equal("v2-branch", stack.Current);
    }
}
