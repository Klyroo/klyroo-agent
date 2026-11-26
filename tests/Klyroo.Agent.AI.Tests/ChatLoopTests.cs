using System.Runtime.CompilerServices;
using Klyroo.Agent.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Klyroo.Agent.AI.Tests;

/// <summary>A fake model: each call to GetResponseAsync pops the next scripted reply, which can read the conversation so far.</summary>
internal sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatMessage>[] script) : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatMessage>> _script = new(script);

    public List<ChatOptions?> Options { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Options.Add(options);
        return Task.FromResult(new ChatResponse(_script.Dequeue()(messages.ToList())));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

public sealed class ChatLoopTests
{
    private static ChatMessage Call(string id, string tool, string json) =>
        new(ChatRole.Assistant, [new FunctionCallContent(id, tool, System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json))]);

    private static string ToolResultText(IReadOnlyList<ChatMessage> messages) =>
        string.Join("\n", messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString()));

    private static (AgentBridgeService Bridge, FormController Form, int[] Submits) Refund()
    {
        var submits = new int[1];
        var form = new FormController(new FormSchema
        {
            FormKey = "refund",
            Title = "Refund",
            Fields =
            [
                new FormFieldSchema { Key = "amount", Label = "Amount", Kind = FormFieldKind.Number },
                new FormFieldSchema { Key = "reason", Label = "Reason", Kind = FormFieldKind.Select, Options = [new("damaged", "Damaged"), new("late", "Late")] },
            ],
            OnSubmit = _ =>
            {
                submits[0]++;
                return Task.CompletedTask;
            },
        });
        var bridge = new AgentBridgeService();
        bridge.RegisterForm(form, confirmSubmit: true);
        return (bridge, form, submits);
    }

    private static IChatClient Chat(AgentBridgeService bridge, ScriptedChatClient model) =>
        new ChatClientBuilder(model).UseFunctionInvocation().Build();

    [Fact]
    public async Task A_chat_with_function_invocation_fills_the_form_and_asks_approval_before_submitting()
    {
        var (bridge, form, submits) = Refund();
        var model = new ScriptedChatClient(
            _ => Call("1", "snapshot", "{}"),
            _ => Call("2", "act", """{ "steps": [ { "ref": "Amount", "action": "setValue", "args": [30] }, { "ref": "Reason", "action": "setValue", "args": ["late"] } ] }"""),
            _ => Call("3", "act_with_approval", """{ "steps": [ { "ref": "Refund", "action": "submit" } ] }"""),
            messages => new ChatMessage(ChatRole.Assistant, $"Done. {ToolResultText(messages).Length > 0}"));
        using var chat = Chat(bridge, model);
        var options = new ChatOptions { Tools = [.. bridge.AsAIFunctions()] };

        var first = await chat.Respond([new ChatMessage(ChatRole.User, "Refund 30 euros, the parcel was late.")], options);

        // The loop ran snapshot and act, then stopped at the approval-required call without running it.
        var request = Assert.Single(first.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal("act_with_approval", Assert.IsType<FunctionCallContent>(request.ToolCall).Name);
        Assert.Equal(30.0, form.GetValue("amount"));
        Assert.Equal("late", form.GetValue("reason"));
        Assert.Equal(0, submits[0]);

        // The user approves in the host UI; the reply goes back through the same client.
        var history = new List<ChatMessage> { new(ChatRole.User, "Refund 30 euros, the parcel was late.") };
        history.AddRange(first.Messages);
        history.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(approved: true)]));
        var second = await chat.Respond(history, options);

        Assert.Equal(1, submits[0]);
        Assert.StartsWith("Done.", second.Text);
    }

    [Fact]
    public async Task A_rejected_approval_does_not_submit()
    {
        var (bridge, _, submits) = Refund();
        var model = new ScriptedChatClient(
            _ => Call("1", "act_with_approval", """{ "steps": [ { "ref": "Refund", "action": "submit" } ] }"""),
            _ => new ChatMessage(ChatRole.Assistant, "The user declined."));
        using var chat = Chat(bridge, model);
        var options = new ChatOptions { Tools = [.. bridge.AsAIFunctions()] };
        var prompt = new ChatMessage(ChatRole.User, "Submit it.");

        var first = await chat.Respond([prompt], options);
        var request = Assert.Single(first.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
        var history = new List<ChatMessage> { prompt };
        history.AddRange(first.Messages);
        history.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(approved: false, reason: "not now")]));

        var second = await chat.Respond(history, options);

        Assert.Equal(0, submits[0]);
        Assert.Equal("The user declined.", second.Text);
    }

    [Fact]
    public async Task A_model_that_calls_act_for_a_confirmation_step_gets_the_instruction_back_and_recovers()
    {
        var (bridge, _, submits) = Refund();
        var model = new ScriptedChatClient(
            _ => Call("1", "act", """{ "steps": [ { "ref": "Refund", "action": "submit" } ] }"""),
            messages =>
            {
                // The tool result of the refused call tells the model to use act_with_approval.
                Assert.Contains("act_with_approval", ToolResultText(messages));
                return Call("2", "act_with_approval", """{ "steps": [ { "ref": "Refund", "action": "submit" } ] }""");
            });
        using var chat = Chat(bridge, model);
        var options = new ChatOptions { Tools = [.. bridge.AsAIFunctions()] };

        var response = await chat.Respond([new ChatMessage(ChatRole.User, "Submit.")], options);

        Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(0, submits[0]);
    }

    [Fact]
    public async Task With_UseBridgePolicy_the_policy_decides_and_no_approval_request_is_raised()
    {
        var (bridge, _, submits) = Refund();
        bridge.AllowPolicy = (_, _) => Task.FromResult(true);
        var model = new ScriptedChatClient(
            _ => Call("1", "act", """{ "steps": [ { "ref": "Refund", "action": "submit" } ] }"""),
            _ => new ChatMessage(ChatRole.Assistant, "Submitted."));
        using var chat = Chat(bridge, model);
        var options = new ChatOptions { Tools = [.. bridge.AsAIFunctions(new AgentAIFunctionOptions { Confirmation = AgentConfirmationMode.UseBridgePolicy })] };

        var response = await chat.Respond([new ChatMessage(ChatRole.User, "Submit.")], options);

        Assert.Empty(response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(1, submits[0]);
        Assert.Equal("Submitted.", response.Text);
    }

    [Fact]
    public async Task The_tool_definitions_reach_the_model_with_their_schemas()
    {
        var (bridge, _, _) = Refund();
        var model = new ScriptedChatClient(_ => new ChatMessage(ChatRole.Assistant, "ok"));
        using var chat = Chat(bridge, model);
        var options = new ChatOptions { Tools = [.. bridge.AsAIFunctions()] };

        await chat.Respond([new ChatMessage(ChatRole.User, "hi")], options);

        var tools = Assert.Single(model.Options)!.Tools!.OfType<AIFunctionDeclaration>().ToList();
        Assert.Equal(["snapshot", "query", "act", "act_with_approval"], tools.Select(t => t.Name));
        Assert.All(tools, t => Assert.Equal("object", t.JsonSchema.GetProperty("type").GetString()));
    }
}
