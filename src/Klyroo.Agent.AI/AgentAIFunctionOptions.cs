namespace Klyroo.Agent.AI;

/// <summary>How the AI functions treat actions that need confirmation. See <see cref="AgentAIFunctionOptions.Confirmation"/>.</summary>
public enum AgentConfirmationMode
{
    /// <summary>
    /// The <c>act</c> function refuses steps that need confirmation and tells the model to call
    /// <c>act_with_approval</c>, a second function wrapped in <see cref="Microsoft.Extensions.AI.ApprovalRequiredAIFunction"/>.
    /// A <c>FunctionInvokingChatClient</c> then asks its caller for approval before that function runs.
    /// </summary>
    RequireApproval,

    /// <summary>
    /// Only <c>snapshot</c>, <c>query</c> and <c>act</c> are returned, and <c>act</c> leaves the decision about steps that
    /// need confirmation to <see cref="AgentBridgeService.AllowPolicy"/>, which refuses them when it is not set. Use it when
    /// your own code already decides, for example a policy that asks the user in your UI.
    /// </summary>
    UseBridgePolicy,
}

/// <summary>Options for <see cref="AgentBridgeAIExtensions.AsAIFunctions"/>.</summary>
public sealed class AgentAIFunctionOptions
{
    /// <summary>Creates options with <see cref="AgentConfirmationMode.RequireApproval"/>.</summary>
    public AgentAIFunctionOptions()
    {
    }

    /// <summary>How actions that need confirmation are handled. The default is <see cref="AgentConfirmationMode.RequireApproval"/>.</summary>
    public AgentConfirmationMode Confirmation { get; init; } = AgentConfirmationMode.RequireApproval;
}
