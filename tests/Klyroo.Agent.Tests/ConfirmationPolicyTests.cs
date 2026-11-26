using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class ConfirmationPolicyTests
{
    private static (AgentBridgeService Agent, DynamicFormController Form, int[] Submits) ConfirmedForm()
    {
        var submits = new int[1];
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "pay",
            Title = "Payment",
            Fields = [new DynamicFieldSchema { Key = "amount", Label = "Amount", Kind = FormFieldKind.Number }],
            OnSubmit = _ =>
            {
                submits[0]++;
                return Task.CompletedTask;
            },
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: true);
        return (agent, form, submits);
    }

    [Fact]
    public async Task A_confirmation_required_step_is_refused_when_no_policy_is_set()
    {
        var (agent, _, submits) = ConfirmedForm();

        var result = await agent.ActAsync([new AgentActStep("Payment", "submit")]);

        Assert.False(result.Ok);
        Assert.Contains("needs confirmation and no AllowPolicy is set", result.Error);
        Assert.Equal(0, submits[0]);
    }

    [Fact]
    public async Task Steps_that_do_not_need_confirmation_run_without_a_policy()
    {
        var (agent, form, _) = ConfirmedForm();

        var result = await agent.ActAsync([new AgentActStep("Amount", "setValue", [12])]);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(12.0, form.GetValue("amount"));
    }

    [Fact]
    public async Task An_AllowPolicy_that_approves_lets_the_confirmed_step_run_and_sees_the_flag()
    {
        var (agent, _, submits) = ConfirmedForm();
        var flags = new List<bool>();
        agent.AllowPolicy = (_, confirm) =>
        {
            flags.Add(confirm);
            return Task.FromResult(true);
        };

        var result = await agent.ActAsync([
            new AgentActStep("Amount", "setValue", [5]),
            new AgentActStep("Payment", "submit"),
        ]);

        Assert.True(result.Ok, result.Error);
        Assert.Equal([false, true], flags);
        Assert.Equal(1, submits[0]);
    }

    [Fact]
    public async Task A_per_call_allow_counts_as_a_policy()
    {
        var (agent, _, submits) = ConfirmedForm();

        var refused = await agent.ActAsync([new AgentActStep("Payment", "submit")], (_, _) => Task.FromResult(false));
        Assert.False(refused.Ok);
        Assert.Contains("refused by caller policy", refused.Error);
        Assert.Equal(0, submits[0]);

        var approved = await agent.ActAsync([new AgentActStep("Payment", "submit")], (_, confirm) => Task.FromResult(confirm));
        Assert.True(approved.Ok, approved.Error);
        Assert.Equal(1, submits[0]);
    }

    [Fact]
    public async Task A_policy_that_refuses_confirmed_steps_blocks_them()
    {
        var (agent, _, submits) = ConfirmedForm();
        agent.AllowPolicy = (_, confirm) => Task.FromResult(!confirm);

        var result = await agent.ActAsync([new AgentActStep("Payment", "submit")]);

        Assert.False(result.Ok);
        Assert.Contains("refused by policy", result.Error);
        Assert.Equal(0, submits[0]);
    }

    [Fact]
    public async Task A_spreadsheet_submit_and_clearFile_are_refused_by_default_and_editCell_is_not()
    {
        var importer = new SpreadsheetImporter<PersonRow>(TestSheets.Names("people", onSubmit: _ => Task.FromResult<IReadOnlyList<SpreadsheetRejectedRow>?>([])));
        importer.LoadText("Name\nAda", "p.csv");
        var agent = new AgentBridgeService();
        agent.RegisterSpreadsheet(importer);

        Assert.True((await agent.ActAsync([new AgentActStep("people", "editCell", [2, "name", "Ada L."])])).Ok);

        var submit = await agent.ActAsync([new AgentActStep("people", "submit")]);
        Assert.False(submit.Ok);
        Assert.Contains("needs confirmation", submit.Error);
        Assert.Equal(SpreadsheetSubmitStatus.Idle, importer.SubmitStatus);

        var clear = await agent.ActAsync([new AgentActStep("people", "clearFile")]);
        Assert.False(clear.Ok);
        Assert.True(importer.FileLoaded);
    }

    [Fact]
    public async Task Moving_between_steps_of_a_form_never_needs_confirmation()
    {
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "wizard",
            Title = "Wizard",
            Steps =
            [
                new DynamicStepSchema { Key = "one", Title = "One", Fields = [new DynamicFieldSchema { Key = "a", Label = "A" }] },
                new DynamicStepSchema { Key = "two", Title = "Two", Fields = [new DynamicFieldSchema { Key = "b", Label = "B" }] },
            ],
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form);

        var result = await agent.ActAsync([new AgentActStep("Wizard", "nextStep")]);

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void The_snapshot_reports_which_actions_need_confirmation()
    {
        var (agent, _, _) = ConfirmedForm();
        var form = agent.Snapshot().Single(s => s.Component == "form");
        var field = agent.Snapshot().Single(s => s.Component == "number");

        // Submit and reset need confirmation; the form component itself does not, so moving between steps never asks.
        Assert.False(form.ConfirmRequired);
        Assert.Equal(["submit", "reset"], form.ConfirmActions);
        Assert.False(field.ConfirmRequired);
        Assert.Empty(field.ConfirmActions!);
    }

    [Fact]
    public async Task A_confirmation_target_addressed_by_its_ref_is_gated_like_one_addressed_by_label()
    {
        var (agent, _, submits) = ConfirmedForm();
        var formRef = agent.Snapshot().Single(c => c.Component == "form").Ref;

        var refused = await agent.ActAsync([new AgentActStep(formRef, "submit")]);
        Assert.False(refused.Ok);
        Assert.Contains("needs confirmation", refused.Error);
        Assert.Equal(0, submits[0]);

        agent.AllowPolicy = (_, confirm) => Task.FromResult(confirm);
        var approved = await agent.ActAsync([new AgentActStep(formRef, "submit")]);
        Assert.True(approved.Ok, approved.Error);
        Assert.Equal(1, submits[0]);
    }

    [Fact]
    public async Task The_per_call_gate_runs_before_AllowPolicy_so_a_refused_step_never_prompts()
    {
        var (agent, _, submits) = ConfirmedForm();
        var prompted = new List<string>();
        agent.AllowPolicy = (step, _) =>
        {
            prompted.Add(step.Action);
            return Task.FromResult(true);
        };

        var result = await agent.ActAsync(
            [new AgentActStep("Amount", "setValue", [3]), new AgentActStep("Payment", "submit")],
            (_, confirm) => Task.FromResult(!confirm));

        Assert.False(result.Ok);
        Assert.Contains("refused by caller policy", result.Error);
        Assert.Equal(["setValue"], prompted);
        Assert.Equal(0, submits[0]);
    }

    [Fact]
    public async Task A_form_registered_with_the_default_requires_confirmation_to_submit()
    {
        var submits = 0;
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "f", Title = "F",
            Fields = [new DynamicFieldSchema { Key = "a", Label = "A" }],
            OnSubmit = _ =>
            {
                submits++;
                return Task.CompletedTask;
            },
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form);

        var result = await agent.ActAsync([new AgentActStep("F", "submit")]);

        Assert.False(result.Ok);
        Assert.Equal(0, submits);
        Assert.Contains("submit", agent.Snapshot().Single(c => c.Component == "form").ConfirmActions!);
    }

    [Fact]
    public async Task A_call_with_more_than_the_step_cap_runs_nothing()
    {
        var (agent, form, _) = ConfirmedForm();
        var tooMany = Enumerable.Range(0, AgentBridgeService.MaxStepsPerAct + 1).Select(i => new AgentActStep("Amount", "setValue", [i])).ToList();

        var refused = await agent.ActAsync(tooMany);

        Assert.False(refused.Ok);
        Assert.Contains("Too many steps", refused.Error);
        Assert.Empty(refused.Steps);
        Assert.Null(form.GetValue("amount"));

        var atCap = await agent.ActAsync(tooMany.Take(AgentBridgeService.MaxStepsPerAct).ToList());
        Assert.True(atCap.Ok, atCap.Error);
        Assert.Equal(49.0, form.GetValue("amount"));
    }

    [Fact]
    public async Task The_cap_also_applies_to_tool_calls()
    {
        var (agent, form, _) = ConfirmedForm();
        var steps = string.Join(",", Enumerable.Range(0, 51).Select(i => $$"""{ "ref": "Amount", "action": "setValue", "args": [{{i}}] }"""));

        var result = await agent.InvokeToolAsync("act", System.Text.Json.JsonDocument.Parse($$"""{ "steps": [{{steps}}] }""").RootElement.Clone());

        Assert.False(Assert.IsType<AgentActResult>(result).Ok);
        Assert.Null(form.GetValue("amount"));
    }
}
