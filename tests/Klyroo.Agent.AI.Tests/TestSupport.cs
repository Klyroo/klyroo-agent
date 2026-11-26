using Microsoft.Extensions.AI;
using Xunit;

namespace Klyroo.Agent.AI.Tests;

/// <summary>Calls that pass the cancellation token of the running test, as xUnit asks.</summary>
internal static class TestSupport
{
    public static ValueTask<object?> Run(this AIFunction function, AIFunctionArguments arguments, CancellationToken? token = null) =>
        function.InvokeAsync(arguments, token ?? TestContext.Current.CancellationToken);

    public static Task<ChatResponse> Respond(this IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options) =>
        client.GetResponseAsync(messages, options, TestContext.Current.CancellationToken);
}
