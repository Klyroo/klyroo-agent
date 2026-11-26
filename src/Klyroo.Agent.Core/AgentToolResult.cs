namespace Klyroo.Agent;

/// <summary>
/// What <c>AgentBridgeService.InvokeToolAsync</c> returns: one of <see cref="AgentSnapshotResult"/>,
/// <see cref="AgentQueryResult"/>, <see cref="AgentActResult"/> or <see cref="AgentToolError"/>. Match on the subtype.
/// </summary>
public abstract record AgentToolResult;

/// <summary>The result of the <c>snapshot</c> tool.</summary>
/// <param name="Components">The registered components, as <c>AgentBridgeService.Snapshot</c> returns them.</param>
public sealed record AgentSnapshotResult(IReadOnlyList<AgentComponentSnapshot> Components) : AgentToolResult;

/// <summary>The result of the <c>query</c> tool.</summary>
/// <param name="Matches">The matching components, as <c>AgentBridgeService.Query</c> returns them.</param>
public sealed record AgentQueryResult(IReadOnlyList<AgentQueryMatch> Matches) : AgentToolResult;
