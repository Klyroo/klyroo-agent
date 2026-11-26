using System.Text.Json;
using Klyroo.Spreadsheet;
using Microsoft.Extensions.AI;
using Xunit;

namespace Klyroo.Agent.AI.Tests;

/// <summary>
/// The JSON the AI functions return did not change when the bridge was typed. The files in <c>Golden</c> were written by the
/// untyped bridge (object values, dictionary records) for this same scenario, one file per call; each call here must give the
/// same text, byte for byte. The calls run in order on one bridge, so each one sees what the earlier ones changed.
/// One value differs on purpose: the untyped bridge reported <c>selected: false</c> for every option of a multiple choice (it compared
/// the text of the list with the option), so <c>query-all.json</c> was edited by hand to <c>selected: true</c> for the chosen option "cheese".
/// A second value differs on purpose: the untyped bridge let the model write the password field "Secret", the typed one refuses it unless the application opts in
/// (<c>allowSensitiveWrites</c>), so <c>act-form-ok.json</c> ends with the refusal of step 6 instead of a successful write.
/// </summary>
public sealed class JsonCompatibilityTests
{
    public sealed class Person
    {
        public string? Name { get; set; }

        public double? Age { get; set; }

        public string? Team { get; set; }

        public string? Id { get; set; }
    }

    private static string GoldenPath(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name + ".json");

    [Fact]
    public async Task Every_tool_returns_the_JSON_the_untyped_bridge_returned()
    {
        var bridge = new AgentBridgeService();
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "order", Title = "Order",
            Steps =
            [
                new DynamicStepSchema
                {
                    Key = "one", Title = "One",
                    Fields =
                    [
                        new DynamicFieldSchema { Key = "name", Label = "Name", DefaultValue = "Ada", Required = true },
                        new DynamicFieldSchema { Key = "qty", Label = "Quantity", Kind = FormFieldKind.Number, DefaultValue = 3.0 },
                        new DynamicFieldSchema { Key = "agree", Label = "Agree", Kind = FormFieldKind.Checkbox, DefaultValue = true },
                        new DynamicFieldSchema { Key = "size", Label = "Size", Kind = FormFieldKind.Select, DefaultValue = "m", Options = [new("s", "Small"), new("m", "Medium"), new("l", "Large", Disabled: true)] },
                        new DynamicFieldSchema { Key = "toppings", Label = "Toppings", Kind = FormFieldKind.CheckboxGroup, Options = [new("cheese", "Cheese"), new("olives", "Olives")], DefaultValue = new List<string> { "cheese" } },
                        new DynamicFieldSchema { Key = "secret", Label = "Secret", Kind = FormFieldKind.Password, DefaultValue = "hunter2" },
                    ],
                },
                new DynamicStepSchema
                {
                    Key = "two", Title = "Two",
                    Fields =
                    [
                        new DynamicFieldSchema { Key = "born", Label = "Born", Kind = FormFieldKind.Date, DefaultValue = "2000-01-31" },
                        new DynamicFieldSchema { Key = "empty", Label = "Empty" },
                    ],
                },
            ],
        });
        bridge.RegisterForm(form);

        var dashboard = new DashboardController("sales");
        dashboard.AddView("overview", "Overview");
        dashboard.AddView("cohorts", "Cohorts");
        dashboard.AddFilter(new DashboardFilterControl("region", "Region", items: [new("emea", "EMEA"), new("amer", "AMER")], defaultValues: ["emea"]));
        dashboard.AddFilter(new DashboardFilterControl("channel", "Channel", items: [new("web", "Web"), new("shop", "Shop")], defaultValues: ["web"], multiple: true));
        bridge.RegisterDashboard(dashboard);

        var importer = new SpreadsheetImporter<Person>(SpreadsheetSchema.For<Person>(s =>
        {
            s.Key = "people";
            s.Column(r => r.Name, c => c.Key("name").Label("Name").Required().Headers("Full name"));
            s.Column(r => r.Age, c => c.Key("age").Label("Age").Headers("Years"));
            s.Column(r => r.Team, c => c.Key("team").Label("Team").Options([new SpreadsheetOption<string?>("engine", "Engine"), new SpreadsheetOption<string?>("ops", "Operations")]));
            s.Column(r => r.Id, c => c.Key("id").Label("Id").Editable(false).Headers("Never"));
        }));
        importer.LoadText("Full name;Years;Team\nAda;36;Engine\nGrace;abc;Engine\nLinus;54;Kernel", "people.csv");
        bridge.RegisterSpreadsheet(importer);

        var functions = bridge.AsAIFunctions().ToDictionary(f => f.Name);
        var mismatches = new List<string>();

        async Task Check(string name, string tool, string json)
        {
            var arguments = new AIFunctionArguments();
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
            var result = Assert.IsType<JsonElement>(await functions[tool].Run(arguments));
            var expected = await File.ReadAllTextAsync(GoldenPath(name), TestContext.Current.CancellationToken);
            if (result.GetRawText() != expected) mismatches.Add($"{name}\n  expected: {expected}\n  actual:   {result.GetRawText()}");
        }

        await Check("snapshot-all", "snapshot", "{}");
        await Check("snapshot-no-offpage", "snapshot", """{"includeOffPage":false}""");
        await Check("query-all", "query", "{}");
        await Check("query-kind-select", "query", """{"kind":"select"}""");
        await Check("query-within-size", "query", """{"within":"Size"}""");
        await Check("query-within-size-text", "query", """{"within":"Size","text":"med"}""");
        await Check("query-within-region", "query", """{"within":"Region"}""");
        await Check("query-within-dashboard", "query", """{"within":"sales"}""");
        await Check("query-unknown", "query", """{"within":"nothing"}""");
        foreach (var collection in new[] { "rows", "issues", "columns", "headers", "questions" })
        {
            await Check("query-sheet-" + collection, "query", $$"""{"within":"people","collection":"{{collection}}"}""");
        }
        await Check("query-sheet-rows-invalid", "query", """{"within":"people","collection":"rows","filter":"invalid","limit":1}""");
        await Check("query-sheet-nothing", "query", """{"within":"people","collection":"nothing"}""");
        await Check("act-form-ok", "act", """{"steps":[{"ref":"Name","action":"setValue","args":["Grace"]},{"ref":"Quantity","action":"setValue","args":[12.5]},{"ref":"Agree","action":"toggle"},{"ref":"Size","action":"setValue","args":["Small"]},{"ref":"Toppings","action":"setValue","args":[["olives","cheese"]]},{"ref":"Secret","action":"setValue","args":["pw"]}]}""");
        await Check("act-form-fail", "act", """{"steps":[{"ref":"Name","action":"setValue","args":["Zed"]},{"ref":"Quantity","action":"setValue","args":["abc"]}]}""");
        await Check("act-form-clear", "act", """{"steps":[{"ref":"Name","action":"clear"},{"ref":"Agree","action":"clear"},{"ref":"Toppings","action":"clear"},{"ref":"Quantity","action":"setValue","args":[3]}]}""");
        await Check("act-form-submit-refused", "act", """{"steps":[{"ref":"Order","action":"submit"}]}""");
        await Check("act-form-nextstep", "act", """{"steps":[{"ref":"Order","action":"nextStep"},{"ref":"Born","action":"setValue","args":["2001-02-03"]}]}""");
        await Check("act-dashboard", "act", """{"steps":[{"ref":"Region","action":"setValue","args":["amer"]},{"ref":"Channel","action":"toggle","args":["shop"]},{"ref":"sales","action":"setView","args":["cohorts"]}]}""");
        await Check("act-dashboard-fail", "act", """{"steps":[{"ref":"Region","action":"setValue","args":["mars"]}]}""");
        await Check("act-sheet", "act", """{"steps":[{"ref":"people","action":"editCell","args":[3,"age","41"]},{"ref":"people","action":"answerValue","args":["team","Kernel","map","ops"]},{"ref":"people","action":"discardRows","args":[2]}]}""");
        await Check("act-sheet-fail", "act", """{"steps":[{"ref":"people","action":"editCell","args":[99,"age","5"]}]}""");
        await Check("act-missing", "act", """{"steps":[{"ref":"nope","action":"setValue","args":["x"]}]}""");
        await Check("snapshot-after", "snapshot", "{}");
        await Check("error-unknown-tool", "snapshot", """{"includeOffPage":"no"}""");

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }
}
