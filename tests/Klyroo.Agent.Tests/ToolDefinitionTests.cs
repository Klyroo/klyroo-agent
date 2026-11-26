using System.Text.Json;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class ToolDefinitionTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (AgentBridgeService Agent, FormController Form) Bridge()
    {
        var form = new FormController(new FormSchema
        {
            FormKey = "order",
            Title = "Order",
            Fields =
            [
                new FormFieldSchema { Key = "qty", Label = "Quantity", Kind = FormFieldKind.Number },
                new FormFieldSchema
                {
                    Key = "toppings", Label = "Toppings", Kind = FormFieldKind.CheckboxGroup,
                    Options = [new("cheese", "Cheese"), new("olives", "Olives")],
                },
                new FormFieldSchema { Key = "note", Label = "Note", Condition = f => f.GetValue<double>("qty") > 5 },
            ],
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form);
        return (agent, form);
    }

    [Fact]
    public void Every_tool_definition_carries_an_object_input_schema_with_described_properties()
    {
        var tools = AgentBridgeService.GetToolDefinitions().ToDictionary(t => t.Name);

        Assert.Equal(["snapshot", "query", "act"], tools.Keys);
        foreach (var tool in tools.Values)
        {
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.GetProperty("properties").ValueKind);
        }

        var query = tools["query"].InputSchema.GetProperty("properties");
        foreach (var name in new[] { "within", "text", "kind", "collection", "filter", "offset", "limit" })
        {
            Assert.True(query.TryGetProperty(name, out var property), name);
            Assert.False(string.IsNullOrWhiteSpace(property.GetProperty("description").GetString()), name);
        }
    }

    [Fact]
    public void The_act_schema_requires_steps_with_ref_and_action_like_the_browser_runtime()
    {
        var act = AgentBridgeService.GetToolDefinitions().Single(t => t.Name == "act").InputSchema;

        Assert.Equal(["steps"], act.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        var item = act.GetProperty("properties").GetProperty("steps").GetProperty("items");
        Assert.Equal(["ref", "action"], item.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("array", item.GetProperty("properties").GetProperty("args").GetProperty("type").GetString());
    }

    [Fact]
    public void The_dotnet_schemas_cover_the_properties_of_the_browser_schemas()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Klyroo.Agent", "wwwroot", "klyroo-agent.js"));
        var queryBlock = script[script.IndexOf("name: 'query'", StringComparison.Ordinal)..];
        queryBlock = queryBlock[..queryBlock.IndexOf("name: 'act'", StringComparison.Ordinal)];
        var jsProperties = System.Text.RegularExpressions.Regex.Matches(queryBlock, @"^\s{8}(\w+): \{ type", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(jsProperties);

        var dotnet = AgentBridgeService.GetToolDefinitions().Single(t => t.Name == "query").InputSchema.GetProperty("properties");
        foreach (var name in jsProperties) Assert.True(dotnet.TryGetProperty(name, out _), name);
    }

    [Fact]
    public void Hints_are_unchanged()
    {
        var tools = AgentBridgeService.GetToolDefinitions().ToDictionary(t => t.Name);
        Assert.True(tools["snapshot"].ReadOnlyHint);
        Assert.True(tools["query"].ReadOnlyHint);
        Assert.True(tools["act"].ConsequentialHint);
        Assert.All(tools.Values, t => Assert.True(t.UntrustedContentHint));
    }

    [Fact]
    public async Task InvokeTool_passes_typed_JSON_arguments_through_to_the_fields()
    {
        var (agent, form) = Bridge();

        var result = await agent.InvokeToolAsync("act", Json("""
            { "steps": [
                { "ref": "Quantity", "action": "setValue", "args": [7] },
                { "ref": "Toppings", "action": "setValue", "args": [["cheese", "Olives"]] }
            ] }
            """));

        var act = Assert.IsType<AgentActResult>(result);
        Assert.True(act.Ok, act.Error);
        Assert.Equal(7.0, form.GetValue("qty"));
        Assert.Equal(["cheese", "olives"], Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("toppings")));
    }

    [Fact]
    public async Task InvokeTool_snapshot_honors_includeOffPage_and_query_reads_all_its_arguments()
    {
        var (agent, form) = Bridge();

        var visible = Assert.IsAssignableFrom<IReadOnlyList<AgentComponentSnapshot>>(await agent.InvokeToolAsync("snapshot", Json("""{ "includeOffPage": false }""")));
        Assert.DoesNotContain(visible, c => c.Label == "Note");
        var all = Assert.IsAssignableFrom<IReadOnlyList<AgentComponentSnapshot>>(await agent.InvokeToolAsync("snapshot", default));
        Assert.DoesNotContain(all, c => c.Label == "Note"); // its condition is false, so it is not in effect
        form.SetValue("qty", 9.0);
        var after = Assert.IsAssignableFrom<IReadOnlyList<AgentComponentSnapshot>>(await agent.InvokeToolAsync("snapshot", default));
        Assert.Contains(after, c => c.Label == "Note");

        var choices = Assert.IsAssignableFrom<IReadOnlyList<AgentQueryMatch>>(await agent.InvokeToolAsync("query", Json("""{ "within": "Toppings", "text": "oliv" }""")));
        Assert.Equal("olives", Assert.Single(Assert.Single(choices).Items).Value);
    }

    [Fact]
    public async Task InvokeTool_query_pages_a_collection()
    {
        var importer = new SpreadsheetImporter(new SpreadsheetSchema { Key = "rows", Columns = [new SpreadsheetColumnSchema { Key = "a", Label = "A" }] });
        importer.LoadText("A\n1\n2\n3", "r.csv");
        var agent = new AgentBridgeService();
        agent.RegisterSpreadsheet(importer);

        var result = Assert.IsAssignableFrom<IReadOnlyList<AgentQueryMatch>>(await agent.InvokeToolAsync("query",
            Json("""{ "within": "rows", "collection": "rows", "limit": 2, "offset": 1 }""")));

        var match = Assert.Single(result);
        Assert.Equal(3, match.Total);
        Assert.Equal([3, 4], match.Records!.Select(r => (int)r["rowNumber"]!));
    }

    [Theory]
    [InlineData("act", """{ }""")]
    [InlineData("act", """{ "steps": "x" }""")]
    [InlineData("act", """{ "steps": [ { "action": "setValue" } ] }""")]
    [InlineData("act", """{ "steps": [ { "ref": 5, "action": "setValue" } ] }""")]
    [InlineData("act", """{ "steps": [ { "ref": "Quantity", "action": "setValue", "args": "7" } ] }""")]
    [InlineData("query", """{ "limit": "ten" }""")]
    [InlineData("query", """{ "within": 3 }""")]
    [InlineData("snapshot", """{ "includeOffPage": "no" }""")]
    [InlineData("snapshot", """[1]""")]
    [InlineData("shred", """{ }""")]
    public async Task A_malformed_call_returns_an_error_value_and_changes_nothing(string tool, string arguments)
    {
        var (agent, form) = Bridge();

        var result = await agent.InvokeToolAsync(tool, Json(arguments));

        var error = Assert.IsType<AgentToolError>(result);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
        Assert.Null(form.GetValue("qty"));
    }

    [Fact]
    public async Task InvokeTool_act_goes_through_the_policy_like_ActAsync()
    {
        var form = new FormController(new FormSchema { FormKey = "pay", Title = "Payment", Fields = [new FormFieldSchema { Key = "a", Label = "A" }] });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: true);
        var call = Json("""{ "steps": [ { "ref": "Payment", "action": "submit" } ] }""");

        var refused = Assert.IsType<AgentActResult>(await agent.InvokeToolAsync("act", call));
        Assert.False(refused.Ok);
        Assert.Contains("needs confirmation", refused.Error);

        var approved = Assert.IsType<AgentActResult>(await agent.InvokeToolAsync("act", call, (_, _) => Task.FromResult(true)));
        Assert.True(approved.Ok, approved.Error);
    }

    [Fact]
    public async Task Dashboard_filters_accept_raw_JSON_lists_from_a_tool_call()
    {
        var dashboard = new DashboardController("sales");
        var region = dashboard.AddFilter(new DashboardFilterControl(
            "region", "Region", items: [new("emea", "EMEA"), new("amer", "AMER")], defaultValues: ["emea"], multiple: true));
        var agent = new AgentBridgeService();
        agent.RegisterDashboard(dashboard);

        var result = Assert.IsType<AgentActResult>(await agent.InvokeToolAsync("act",
            Json("""{ "steps": [ { "ref": "Region", "action": "setValue", "args": [["amer", "emea"]] } ] }""")));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(["amer", "emea"], region.Values.OrderBy(v => v));
    }

    [Fact]
    public async Task Limit_and_offset_written_as_25_0_are_whole_numbers()
    {
        var importer = new SpreadsheetImporter(new SpreadsheetSchema { Key = "rows", Columns = [new SpreadsheetColumnSchema { Key = "a", Label = "A" }] });
        importer.LoadText("A\n1\n2\n3", "r.csv");
        var agent = new AgentBridgeService();
        agent.RegisterSpreadsheet(importer);

        var result = Assert.IsAssignableFrom<IReadOnlyList<AgentQueryMatch>>(await agent.InvokeToolAsync("query",
            Json("""{ "within": "rows", "collection": "rows", "limit": 2.0, "offset": 1.0 }""")));

        Assert.Equal([3, 4], Assert.Single(result).Records!.Select(r => (int)r["rowNumber"]!));
        Assert.IsType<AgentToolError>(await agent.InvokeToolAsync("query", Json("""{ "limit": 2.5 }""")));
    }
}
