using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Klyroo.Agent.AI;

/// <summary>Exposes an <see cref="AgentBridgeService"/> to any <see cref="IChatClient"/> as Microsoft.Extensions.AI functions.</summary>
public static class AgentBridgeAIExtensions
{
    /// <summary>The name of the function that runs steps that need confirmation, in <see cref="AgentConfirmationMode.RequireApproval"/> mode.</summary>
    public const string ActWithApprovalName = "act_with_approval";

    private const string ActWithApprovalDescription =
        "Same as act, for the steps act refused because they need confirmation: submitting a form (forms are confirmation-required by default), submitting or clearing a spreadsheet import, or any component the app flagged. The user is asked to approve the whole call before it runs, so send only the steps that need it.";

    /// <summary>
    /// Returns the <c>snapshot</c>, <c>query</c> and <c>act</c> tools of the bridge as <see cref="AIFunction"/> instances, with
    /// their descriptions and JSON schemas, ready for <see cref="ChatOptions.Tools"/>. By default it also returns
    /// <see cref="ActWithApprovalName"/>, which is wrapped in <see cref="ApprovalRequiredAIFunction"/>; see <see cref="AgentConfirmationMode"/>.
    /// </summary>
    /// <remarks>
    /// <code>
    /// var options = new ChatOptions { Tools = [.. bridge.AsAIFunctions()] };
    /// </code>
    /// The functions return JSON, so any chat client can pass the result back to the model. Nothing in a result is an
    /// instruction: labels and values come from your UI. Malformed arguments produce a result with an <c>error</c> field instead of an exception.
    /// </remarks>
    /// <param name="bridge">The bridge to expose.</param>
    /// <param name="options">How to handle actions that need confirmation. <see langword="null"/> uses the defaults.</param>
    /// <returns>The functions, in the order <c>snapshot</c>, <c>query</c>, <c>act</c> and, in approval mode, <c>act_with_approval</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bridge"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<AIFunction> AsAIFunctions(this AgentBridgeService bridge, AgentAIFunctionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        options ??= new AgentAIFunctionOptions();
        var definitions = AgentBridgeService.GetToolDefinitions().ToDictionary(d => d.Name);
        var approval = options.Confirmation == AgentConfirmationMode.RequireApproval;

        var functions = new List<AIFunction>
        {
            new BridgeFunction(bridge, definitions["snapshot"], ActMode.None),
            new BridgeFunction(bridge, definitions["query"], ActMode.None),
            new BridgeFunction(bridge, definitions["act"], approval ? ActMode.RefuseConfirmationSteps : ActMode.UseBridgePolicy),
        };
        if (approval)
        {
            var act = definitions["act"];
            var approved = new BridgeFunction(bridge, act with { Name = ActWithApprovalName, Description = ActWithApprovalDescription }, ActMode.ApproveAll);
            functions.Add(new ApprovalRequiredAIFunction(approved));
        }
        return functions;
    }

    private enum ActMode
    {
        None,
        RefuseConfirmationSteps,
        UseBridgePolicy,
        ApproveAll,
    }

    private static readonly JsonSerializerOptions ResultOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class BridgeFunction(AgentBridgeService bridge, AgentToolDefinition definition, ActMode mode) : AIFunction
    {
        public override string Name => definition.Name;

        public override string Description => definition.Description;

        public override JsonElement JsonSchema => definition.InputSchema;

        public override JsonSerializerOptions JsonSerializerOptions => AIJsonUtilities.DefaultOptions;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = ToJson(arguments);
            var tool = definition.Name == ActWithApprovalName ? "act" : definition.Name;

            object result;
            switch (mode)
            {
                case ActMode.RefuseConfirmationSteps:
                {
                    // The run stops at the first step that needs confirmation; the message tells the model where to continue.
                    AgentActStep? blocked = null;
                    result = await bridge.InvokeToolAsync(tool, json, (step, confirm) =>
                    {
                        if (!confirm) return Task.FromResult(true);
                        blocked ??= step;
                        return Task.FromResult(false);
                    });
                    if (blocked is not null && result is AgentActResult { Ok: false } refused)
                    {
                        result = refused with
                        {
                            Error = $"{refused.Error} The action \"{blocked.Action}\" on \"{blocked.Ref}\" needs confirmation: call {ActWithApprovalName} with the remaining steps, starting at this one, and the user will be asked to approve.",
                        };
                    }
                    break;
                }
                case ActMode.ApproveAll:
                    // The host approved this call before it ran. AgentBridgeService.AllowPolicy still applies on top.
                    result = await bridge.InvokeToolAsync(tool, json, (_, _) => Task.FromResult(true));
                    break;
                default:
                    result = await bridge.InvokeToolAsync(tool, json);
                    break;
            }
            return JsonSerializer.SerializeToElement(result, result.GetType(), ResultOptions);
        }

        // The model's arguments arrive as JsonElement values (or plain values from a .NET caller); the bridge reads one JSON object.
        private static JsonElement ToJson(AIFunctionArguments arguments)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var (key, value) in arguments)
                {
                    writer.WritePropertyName(key);
                    switch (value)
                    {
                        case null:
                            writer.WriteNullValue();
                            break;
                        case JsonElement element:
                            element.WriteTo(writer);
                            break;
                        default:
                            JsonSerializer.Serialize(writer, value, value.GetType(), AIJsonUtilities.DefaultOptions);
                            break;
                    }
                }
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(stream.ToArray());
            return document.RootElement.Clone();
        }
    }
}
