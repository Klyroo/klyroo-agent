using Klyroo.Dashboard;
using Klyroo.Form;
using Klyroo.Spreadsheet;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class BridgeActionRulesTests
{
    private static DashboardFilterControl AddRegions(DashboardController dashboard, int? max = null) =>
        dashboard.AddFilter(new DashboardFilterControl(
            "region", "Region",
            items: [new("emea", "EMEA"), new("amer", "AMER"), new("apac", "APAC")],
            defaultValues: ["emea"],
            multiple: true,
            max: max));

    [Fact]
    public async Task A_step_that_throws_fails_the_run_instead_of_throwing()
    {
        var agent = new AgentBridgeService();
        var form = new FormController(new FormSchema
        {
            FormKey = "f",
            Title = "Profile",
            Fields = [new FormFieldSchema { Key = "name", Label = "Name" }],
            OnSubmit = _ => throw new InvalidOperationException("database down"),
        });
        agent.RegisterForm(form, confirmSubmit: false);

        var result = await agent.ActAsync([new AgentActStep("Profile", "submit")]);

        Assert.False(result.Ok);
        Assert.Contains("database down", result.Error);
    }

    [Fact]
    public async Task A_bad_spreadsheet_row_fails_the_step_and_a_good_one_reports_its_row()
    {
        var agent = new AgentBridgeService();
        var importer = new SpreadsheetImporter(new SpreadsheetSchema
        {
            Key = "people",
            Columns = [new SpreadsheetColumnSchema { Key = "name", Label = "Name" }],
        });
        importer.LoadText("Name\nAda\nGrace", "people.csv");
        agent.RegisterSpreadsheet(importer);
        var observed = new List<ObservedChange>();
        using var sub = agent.Observe(observed.Add);

        var bad = await agent.ActAsync([new AgentActStep("people", "editCell", ["first", "name", "X"])]);
        Assert.False(bad.Ok);

        // Row 3 of the file is the second data row: rows are addressed by the number the user sees, not by an index.
        var good = await agent.ActAsync([new AgentActStep("people", "editCell", [3, "name", "Grace Hopper"])]);
        Assert.True(good.Ok, good.Error);
        Assert.Equal("3", Assert.Single(observed).Row);
        Assert.Equal("Grace Hopper", importer.AllRows[1].Cells["name"].Text);
    }

    [Fact]
    public async Task SetValue_replaces_a_multiple_filter_selection_and_clear_empties_it()
    {
        var agent = new AgentBridgeService();
        var dashboard = new DashboardController("sales");
        var region = AddRegions(dashboard);
        agent.RegisterDashboard(dashboard);

        var set = await agent.ActAsync([new AgentActStep("Region", "setValue", [new[] { "amer", "apac" }])]);
        Assert.True(set.Ok, set.Error);
        Assert.Equal(["amer", "apac"], region.Values.OrderBy(v => v));

        // Setting a value that is already selected keeps it selected.
        var again = await agent.ActAsync([new AgentActStep("Region", "setValue", ["amer"])]);
        Assert.True(again.Ok, again.Error);
        Assert.Equal(["amer"], region.Values);

        var clear = await agent.ActAsync([new AgentActStep("Region", "clear")]);
        Assert.True(clear.Ok, clear.Error);
        Assert.Empty(region.Values);
    }

    [Fact]
    public async Task Filter_steps_fail_on_unknown_values_and_when_Max_blocks_them()
    {
        var agent = new AgentBridgeService();
        var dashboard = new DashboardController("sales");
        var region = AddRegions(dashboard, max: 1);
        agent.RegisterDashboard(dashboard);

        Assert.False((await agent.ActAsync([new AgentActStep("Region", "setValue", ["mars"])])).Ok);
        Assert.False((await agent.ActAsync([new AgentActStep("Region", "toggle", ["amer"])])).Ok);
        Assert.Equal(["emea"], region.Values);
    }

    [Fact]
    public async Task Hidden_fields_are_refused_and_disposing_a_registration_removes_it()
    {
        var agent = new AgentBridgeService();
        var form = new FormController(new FormSchema
        {
            FormKey = "f",
            Fields =
            [
                new FormFieldSchema { Key = "company", Label = "Company", Condition = _ => false },
                new FormFieldSchema { Key = "name", Label = "Name" },
            ],
        });
        var registration = agent.RegisterForm(form);

        var hidden = await agent.ActAsync([new AgentActStep("Company", "setValue", ["Acme"])]);
        Assert.False(hidden.Ok);
        Assert.Contains("not displayed", hidden.Error);

        registration.Dispose();
        Assert.Empty(agent.Snapshot());
    }
}
