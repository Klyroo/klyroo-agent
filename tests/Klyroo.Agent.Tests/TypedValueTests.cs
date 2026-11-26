using System.Text.Json;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class TypedValueTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("\"hello\"", typeof(AgentTextValue))]
    [InlineData("12.50", typeof(AgentNumberValue))]
    [InlineData("true", typeof(AgentBooleanValue))]
    [InlineData("[\"a\",\"b\"]", typeof(AgentListValue))]
    public void A_JSON_value_becomes_the_matching_subtype(string json, Type expected) =>
        Assert.IsType(expected, AgentValue.FromJson(Json(json)));

    [Fact]
    public void JSON_null_and_an_undefined_element_are_no_value()
    {
        Assert.Null(AgentValue.FromJson(Json("null")));
        Assert.Null(AgentValue.FromJson(default));
    }

    [Fact]
    public void A_number_keeps_its_digits_and_converts_on_request()
    {
        var number = Assert.IsType<AgentNumberValue>(AgentValue.FromJson(Json("9007199254740993")));

        Assert.Equal("9007199254740993", number.Text);
        Assert.Equal(9007199254740993m, number.Exact);
        Assert.Equal(9007199254740992d, number.Approximate);
        Assert.Null(new AgentNumberValue("1e999").Approximate);
    }

    [Fact]
    public void The_JSON_of_each_value_is_the_JSON_an_agent_reads()
    {
        Assert.Equal("\"Ada\"", JsonSerializer.Serialize(AgentValue.From("Ada")));
        Assert.Equal("true", JsonSerializer.Serialize(AgentValue.From(true)));
        Assert.Equal("12.50", JsonSerializer.Serialize<AgentValue>(new AgentNumberValue("12.50")));
        Assert.Equal("[\"a\",\"b\"]", JsonSerializer.Serialize(AgentValue.From(["a", "b"])));
        Assert.Equal(
            """{"step":"file","fileLoaded":false,"rows":0,"importableRows":0,"invalidRows":0,"discardedRows":0,"rowsWithWarnings":0,"openQuestions":0,"unmappedRequiredColumns":["Name"],"canSubmit":false,"submitStatus":"Idle","importedCount":0}""",
            JsonSerializer.Serialize<AgentValue>(new AgentSpreadsheetSummary("file", false, null, 0, 0, 0, 0, 0, 0, ["Name"], false, "Idle", 0, null)));
    }

    [Fact]
    public void A_value_has_a_short_text_for_a_label()
    {
        Assert.Equal("Ada", AgentValue.From("Ada").ToDisplayString());
        Assert.Equal("true", AgentValue.From(true).ToDisplayString());
        Assert.Equal("a, b", AgentValue.From(["a", "b"]).ToDisplayString());
        Assert.Equal("3", new AgentNumberValue("3").ToDisplayString());
    }

    [Fact]
    public void Plain_values_convert_to_arguments_that_keep_their_digits()
    {
        Assert.Equal("\"Ada\"", ((AgentArgument)"Ada").ToString());
        Assert.Equal("12", ((AgentArgument)12).ToString());
        Assert.Equal("9223372036854775807", ((AgentArgument)long.MaxValue).ToString());
        Assert.Equal("1.50", ((AgentArgument)1.50m).ToString());
        Assert.Equal("12.5", ((AgentArgument)12.5).ToString());
        Assert.Equal("true", ((AgentArgument)true).ToString());
        Assert.Equal("[\"a\",\"b\"]", ((AgentArgument)new[] { "a", "b" }).ToString());
        Assert.Equal("[1,2]", AgentArgument.List(new[] { 1, 2 }).ToString());
        Assert.Equal("[\"a\",3]", AgentArgument.List("a", 3).ToString());
    }

    [Fact]
    public void An_argument_with_no_value_is_JSON_null_and_a_value_that_JSON_cannot_hold_is_text()
    {
        Assert.Equal("null", ((AgentArgument)(string?)null).ToString());
        Assert.Equal("null", default(AgentArgument).ToString());
        Assert.Equal(JsonValueKind.Null, AgentArgument.Null.Json.ValueKind);
        Assert.Equal(JsonValueKind.String, ((AgentArgument)double.NaN).Json.ValueKind);
        Assert.Equal(JsonValueKind.String, ((AgentArgument)double.PositiveInfinity).Json.ValueKind);
    }

    [Fact]
    public void Arguments_compare_by_their_JSON()
    {
        Assert.True((AgentArgument)5 == Json("5"));
        Assert.True((AgentArgument)"a" != "b");
        Assert.Equal(((AgentArgument)"a").GetHashCode(), ((AgentArgument)"a").GetHashCode());
    }

    [Fact]
    public async Task An_object_argument_fails_the_step_and_nothing_runs()
    {
        var form = new DynamicFormController(new DynamicFormSchema { FormKey = "f", Title = "F", Fields = [new DynamicFieldSchema { Key = "name", Label = "Name", DefaultValue = "Ada" }] });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false);

        var result = await agent.ActAsync([new AgentActStep("Name", "setValue", [Json("""{"a":1}""")])]);

        Assert.False(result.Ok);
        Assert.Contains("An object is not a valid value", result.Error);
        Assert.Equal("Ada", form.GetValue<string>("name"));
    }

    [Fact]
    public async Task Steps_built_from_plain_values_and_from_a_tool_call_run_the_same_way()
    {
        foreach (var useJson in new[] { false, true })
        {
            var form = new DynamicFormController(new DynamicFormSchema { FormKey = "f", Title = "F", Fields = [new DynamicFieldSchema { Key = "qty", Label = "Qty", Kind = FormFieldKind.Number }] });
            var agent = new AgentBridgeService();
            agent.RegisterForm(form, confirmSubmit: false);

            var result = useJson
                ? Assert.IsType<AgentActResult>(await agent.InvokeToolAsync("act", Json("""{"steps":[{"ref":"Qty","action":"setValue","args":[12.5]}]}""")))
                : await agent.ActAsync([new AgentActStep("Qty", "setValue", [12.5])]);

            Assert.True(result.Ok, result.Error);
            Assert.Equal(12.5, form.GetValue<double>("qty"));
            Assert.Equal(new AgentNumberValue("12.5"), Assert.Single(result.Steps).Value);
        }
    }
}
