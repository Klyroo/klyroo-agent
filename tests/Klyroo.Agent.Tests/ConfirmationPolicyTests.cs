using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class ConfirmationPolicyTests
{
    private static (AgentBridgeService Agent, FormController Form, int[] Submits) ConfirmedForm()
    {
        var submits = new int[1];
        var form = new FormController(new FormSchema
        {
            FormKey = "pay",
            Title = "Payment",
            Fields = [new FormFieldSchema { Key = "amount", Label = "Amount", Kind = FormFieldKind.Number }],
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
        var importer = new SpreadsheetImporter(new SpreadsheetSchema
        {
            Key = "people",
            Columns = [new SpreadsheetColumnSchema { Key = "name", Label = "Name" }],
            OnSubmit = _ => Task.FromResult<IReadOnlyList<SpreadsheetRejectedRow>?>([]),
        });
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
    public void The_snapshot_reports_which_actions_need_confirmation()
    {
        var (agent, _, _) = ConfirmedForm();
        var form = agent.Snapshot().Single(s => s.Component == "form");
        var field = agent.Snapshot().Single(s => s.Component == "number");

        Assert.True(form.ConfirmRequired);
        Assert.False(field.ConfirmRequired);
        Assert.Empty(field.ConfirmActions!);
    }
}
