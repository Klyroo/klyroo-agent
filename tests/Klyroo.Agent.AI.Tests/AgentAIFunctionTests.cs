using System.Text.Json;
using Klyroo.Agent;
using Klyroo.Agent.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Klyroo.Agent.AI.Tests;

public sealed class AgentAIFunctionTests
{
    private sealed class Setup
    {
        public required AgentBridgeService Bridge { get; init; }
        public required FormController Form { get; init; }
        public int Submits;
    }

    private static Setup CreateSetup(bool confirmSubmit = true)
    {
        var setup = new Setup
        {
            Bridge = new AgentBridgeService(),
            Form = new FormController(new FormSchema
            {
                FormKey = "refund",
                Title = "Refund",
                Fields =
                [
                    new FormFieldSchema { Key = "amount", Label = "Amount", Kind = FormFieldKind.Number },
                    new FormFieldSchema
                    {
                        Key = "reason", Label = "Reason", Kind = FormFieldKind.Select,
                        Options = [new("damaged", "Damaged"), new("late", "Late")],
                    },
                ],
                OnSubmit = _ => Task.CompletedTask,
            }),
        };
        setup.Bridge.RegisterForm(setup.Form, confirmSubmit);
        return setup;
    }

    private static Setup WithCountedSubmit(bool confirmSubmit = true)
    {
        Setup? setup = null;
        var form = new FormController(new FormSchema
        {
            FormKey = "refund",
            Title = "Refund",
            Fields =
            [
                new FormFieldSchema { Key = "amount", Label = "Amount", Kind = FormFieldKind.Number },
                new FormFieldSchema
                {
                    Key = "reason", Label = "Reason", Kind = FormFieldKind.Select,
                    Options = [new("damaged", "Damaged"), new("late", "Late")],
                },
            ],
            OnSubmit = _ =>
            {
                setup!.Submits++;
                return Task.CompletedTask;
            },
        });
        var bridge = new AgentBridgeService();
        bridge.RegisterForm(form, confirmSubmit);
        setup = new Setup { Bridge = bridge, Form = form };
        return setup;
    }

    private static AIFunction Function(IEnumerable<AIFunction> functions, string name) => functions.Single(f => f.Name == name);

    private static AIFunctionArguments Args(string json)
    {
        var arguments = new AIFunctionArguments();
        foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject())
        {
            arguments[property.Name] = property.Value.Clone();
        }
        return arguments;
    }

    private static JsonElement AsJson(object? value) => Assert.IsType<JsonElement>(value);

    [Fact]
    public void Default_options_return_snapshot_query_act_and_an_approval_required_act()
    {
        var functions = CreateSetup().Bridge.AsAIFunctions();

        Assert.Equal(["snapshot", "query", "act", "act_with_approval"], functions.Select(f => f.Name));
        Assert.IsType<ApprovalRequiredAIFunction>(functions[3]);
        Assert.All(functions.Take(3), f => Assert.IsNotType<ApprovalRequiredAIFunction>(f));
    }

    [Fact]
    public void Functions_carry_the_descriptions_and_input_schemas_of_the_tool_definitions()
    {
        var definitions = AgentBridgeService.GetToolDefinitions().ToDictionary(d => d.Name);
        var functions = CreateSetup().Bridge.AsAIFunctions();

        foreach (var name in new[] { "snapshot", "query", "act" })
        {
            var function = Function(functions, name);
            Assert.Equal(definitions[name].Description, function.Description);
            Assert.Equal(definitions[name].InputSchema.GetRawText(), function.JsonSchema.GetRawText());
        }
        var act = Function(functions, "act").JsonSchema;
        Assert.Equal(["steps"], act.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        var approval = Function(functions, "act_with_approval");
        Assert.Contains("approve", approval.Description);
        Assert.Equal(act.GetRawText(), approval.JsonSchema.GetRawText());
    }

    [Fact]
    public void UseBridgePolicy_returns_three_functions_and_none_requires_approval()
    {
        var functions = CreateSetup().Bridge.AsAIFunctions(new AgentAIFunctionOptions { Confirmation = AgentConfirmationMode.UseBridgePolicy });

        Assert.Equal(["snapshot", "query", "act"], functions.Select(f => f.Name));
        Assert.All(functions, f => Assert.IsNotType<ApprovalRequiredAIFunction>(f));
    }

    [Fact]
    public void A_null_bridge_throws()
    {
        Assert.Throws<ArgumentNullException>(() => AgentBridgeAIExtensions.AsAIFunctions(null!));
    }

    [Fact]
    public async Task Snapshot_and_query_return_JSON_of_the_bridge_results()
    {
        var functions = CreateSetup().Bridge.AsAIFunctions();

        var snapshot = AsJson(await Function(functions, "snapshot").Run(Args("""{ "includeOffPage": false }""")));
        var labels = snapshot.EnumerateArray().Select(c => c.GetProperty("label").GetString()).ToList();
        Assert.Equal(["Amount", "Reason", "Refund"], labels);
        var form = snapshot.EnumerateArray().Single(c => c.GetProperty("component").GetString() == "form");
        Assert.Contains("submit", form.GetProperty("confirmActions").EnumerateArray().Select(a => a.GetString()));

        var query = AsJson(await Function(functions, "query").Run(Args("""{ "within": "Reason", "text": "late" }""")));
        var item = query[0].GetProperty("items")[0];
        Assert.Equal("late", item.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Act_applies_typed_arguments_to_the_form()
    {
        var setup = CreateSetup();
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args("""
            { "steps": [
                { "ref": "Amount", "action": "setValue", "args": [42.5] },
                { "ref": "Reason", "action": "setValue", "args": ["Damaged"] }
            ] }
            """)));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.GetRawText());
        Assert.Equal(42.5, setup.Form.GetValue("amount"));
        Assert.Equal("damaged", setup.Form.GetValue("reason"));
    }

    [Fact]
    public async Task Act_reports_a_rejected_value_with_its_reason_so_the_model_can_retry()
    {
        var setup = CreateSetup();
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args("""
            { "steps": [ { "ref": "Reason", "action": "setValue", "args": ["stolen"] } ] }
            """)));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("not one of the options", result.GetProperty("error").GetString());
        Assert.Null(setup.Form.GetValue("reason"));
    }

    [Fact]
    public async Task Act_refuses_a_confirmation_step_and_points_to_the_approval_function()
    {
        var setup = WithCountedSubmit();
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args("""
            { "steps": [
                { "ref": "Amount", "action": "setValue", "args": [10] },
                { "ref": "Refund", "action": "submit" }
            ] }
            """)));

        Assert.False(result.GetProperty("ok").GetBoolean());
        var error = result.GetProperty("error").GetString();
        Assert.Contains("act_with_approval", error);
        Assert.Contains("submit", error);
        Assert.Equal(0, setup.Submits);
        Assert.Equal(10.0, setup.Form.GetValue("amount"));
    }

    [Fact]
    public async Task Act_runs_a_step_that_needs_no_confirmation_even_on_a_confirmed_form()
    {
        var setup = WithCountedSubmit(confirmSubmit: false);
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args("""
            { "steps": [ { "ref": "Refund", "action": "submit" } ] }
            """)));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.GetRawText());
        Assert.Equal(1, setup.Submits);
    }

    [Fact]
    public async Task The_approval_function_runs_confirmation_steps_when_it_is_invoked()
    {
        var setup = WithCountedSubmit();
        var functions = setup.Bridge.AsAIFunctions();
        var approval = Assert.IsType<ApprovalRequiredAIFunction>(Function(functions, "act_with_approval"));

        // Approval is the caller's job (FunctionInvokingChatClient); invoking the function directly means it was approved.
        var result = AsJson(await approval.Run(Args("""{ "steps": [ { "ref": "Refund", "action": "submit" } ] }""")));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.GetRawText());
        Assert.Equal(1, setup.Submits);
    }

    [Fact]
    public async Task The_bridge_AllowPolicy_still_applies_to_the_approved_function()
    {
        var setup = WithCountedSubmit();
        setup.Bridge.AllowPolicy = (_, _) => Task.FromResult(false);
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act_with_approval").Run(Args("""{ "steps": [ { "ref": "Refund", "action": "submit" } ] }""")));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("refused by policy", result.GetProperty("error").GetString());
        Assert.Equal(0, setup.Submits);
    }

    [Fact]
    public async Task UseBridgePolicy_leaves_the_decision_to_the_AllowPolicy()
    {
        var setup = WithCountedSubmit();
        var functions = setup.Bridge.AsAIFunctions(new AgentAIFunctionOptions { Confirmation = AgentConfirmationMode.UseBridgePolicy });
        var call = Args("""{ "steps": [ { "ref": "Refund", "action": "submit" } ] }""");

        var withoutPolicy = AsJson(await Function(functions, "act").Run(call));
        Assert.False(withoutPolicy.GetProperty("ok").GetBoolean());
        Assert.Contains("needs confirmation", withoutPolicy.GetProperty("error").GetString());
        Assert.Equal(0, setup.Submits);

        setup.Bridge.AllowPolicy = (_, confirm) => Task.FromResult(confirm);
        var withPolicy = AsJson(await Function(functions, "act").Run(Args("""{ "steps": [ { "ref": "Refund", "action": "submit" } ] }""")));
        Assert.True(withPolicy.GetProperty("ok").GetBoolean(), withPolicy.GetRawText());
        Assert.Equal(1, setup.Submits);
    }

    [Fact]
    public async Task Malformed_arguments_return_an_error_field_instead_of_throwing()
    {
        var setup = CreateSetup();
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args("""{ "steps": "set the amount" }""")));

        Assert.Contains("steps", result.GetProperty("error").GetString());
        var empty = AsJson(await Function(functions, "act").Run(new AIFunctionArguments()));
        Assert.True(empty.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Plain_CLR_argument_values_are_accepted_from_a_dotnet_caller()
    {
        var setup = CreateSetup();
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "query").Run(new AIFunctionArguments { ["within"] = "Reason", ["limit"] = 5 }));

        Assert.Equal("Reason", result[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_call_before_the_bridge_runs()
    {
        var setup = CreateSetup();
        var functions = setup.Bridge.AsAIFunctions();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Function(functions, "act").Run(Args("""{ "steps": [ { "ref": "Amount", "action": "setValue", "args": [1] } ] }"""), cts.Token));
        Assert.Null(setup.Form.GetValue("amount"));
    }

    [Fact]
    public async Task In_approval_mode_act_refuses_a_confirmation_step_before_AllowPolicy_is_asked()
    {
        var setup = WithCountedSubmit();
        var asked = new List<string>();
        setup.Bridge.AllowPolicy = (step, _) =>
        {
            asked.Add(step.Action);
            return Task.FromResult(true);
        };
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args("""
            { "steps": [
                { "ref": "Amount", "action": "setValue", "args": [10] },
                { "ref": "Refund", "action": "submit" }
            ] }
            """)));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("act_with_approval", result.GetProperty("error").GetString());
        // The user-facing policy saw the harmless step only: it was never prompted for the step that act refuses.
        Assert.Equal(["setValue"], asked);
        Assert.Equal(0, setup.Submits);

        // After the host approves the call, the policy is asked for the step and still decides.
        asked.Clear();
        var approved = AsJson(await Function(functions, "act_with_approval").Run(Args("""{ "steps": [ { "ref": "Refund", "action": "submit" } ] }""")));
        Assert.True(approved.GetProperty("ok").GetBoolean(), approved.GetRawText());
        Assert.Equal(["submit"], asked);
        Assert.Equal(1, setup.Submits);
    }

    [Fact]
    public async Task A_confirmation_target_addressed_by_ref_is_refused_by_act_too()
    {
        var setup = WithCountedSubmit();
        var formRef = setup.Bridge.Snapshot().Single(c => c.Component == "form").Ref;
        var functions = setup.Bridge.AsAIFunctions();

        var result = AsJson(await Function(functions, "act").Run(Args($$"""{ "steps": [ { "ref": "{{formRef}}", "action": "submit" } ] }""")));

        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("act_with_approval", result.GetProperty("error").GetString());
        Assert.Equal(0, setup.Submits);
    }
}
