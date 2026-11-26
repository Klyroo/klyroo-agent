namespace Klyroo.Agent;

/// <summary>
/// Identifies what caused a state change reported in <see cref="ObservedChange.Source"/>. The .NET
/// <c>AgentBridgeService</c> only raises changes caused by <c>ActAsync</c>, so it always reports <see cref="Agent"/>;
/// the other members let your own code classify changes it forwards through the same record type.
/// </summary>
public enum AgentChangeSource
{
    /// <summary>The change came from keyboard input.</summary>
    Keyboard,
    /// <summary>The change came from a mouse interaction.</summary>
    Mouse,
    /// <summary>The change came from a touch interaction.</summary>
    Touch,
    /// <summary>The change came from a pen interaction.</summary>
    Pen,
    /// <summary>The change came from an assistive technology or a synthetic click.</summary>
    Virtual,
    /// <summary>The change was made by application code rather than by a user or an agent.</summary>
    Programmatic,
    /// <summary>The change was made by an agent through <c>AgentBridgeService.ActAsync</c>.</summary>
    Agent,
}

/// <summary>
/// A notification delivered to subscribers of <c>AgentBridgeService.Observe</c> after an action step succeeds.
/// For password and one-time-code fields, <paramref name="Previous"/> and <paramref name="Value"/> are replaced by
/// <c>"[redacted]"</c>.
/// </summary>
/// <param name="Component">The component kind, for example <c>input</c>, <c>select</c>, <c>form</c> or <c>dashboard</c>.</param>
/// <param name="Ref">The stable reference of the component, for example <c>c3</c>.</param>
/// <param name="Label">The label the component was registered with.</param>
/// <param name="Key">The property that changed. The bridge always reports <c>value</c>.</param>
/// <param name="Previous">The value before the step ran.</param>
/// <param name="Value">The value after the step ran.</param>
/// <param name="Reason">The action name that caused the change, for example <c>setValue</c>.</param>
/// <param name="Source">What caused the change.</param>
/// <param name="Row">The row a step addressed on a tabular component: the user-visible row number of a spreadsheet <c>editCell</c> step, the row numbers of <c>discardRows</c> and <c>restoreRows</c> joined by commas, otherwise <see langword="null"/>.</param>
public sealed record ObservedChange(
    string Component,
    string Ref,
    string Label,
    string Key,
    AgentValue? Previous,
    AgentValue? Value,
    string Reason,
    AgentChangeSource Source,
    string? Row = null);

/// <summary>One registered component as returned by <c>AgentBridgeService.Snapshot</c>.</summary>
/// <param name="Ref">The stable reference (<c>c1</c>, <c>c2</c>, ...) used to address the component in an action step.</param>
/// <param name="Component">The component kind: <c>input</c>, <c>select</c>, <c>checkbox</c>, <c>number</c>, <c>form</c>, <c>dashboard</c> or <c>spreadsheet</c>.</param>
/// <param name="Label">The human-readable label an agent can also use to address the component.</param>
/// <param name="Value">The current value, or the text <c>"[redacted]"</c> for a sensitive field. <see langword="null"/> when the component has no value, such as an empty field.</param>
/// <param name="Actions">The action names the component accepts in <see cref="AgentActStep.Action"/>.</param>
/// <param name="Disabled">Whether the component currently rejects actions.</param>
/// <param name="OffPage">Whether the component is not rendered right now, for example a conditional form field whose condition is false.</param>
/// <param name="ConfirmRequired">Whether every action on the component needs confirmation, because it was registered that way. The bridge passes the flag to <c>AllowPolicy</c>.</param>
/// <param name="ConfirmActions">The actions that need confirmation whatever the component flag says, for example <c>submit</c> and <c>clearFile</c> on a spreadsheet. Empty when the component has none.</param>
public sealed record AgentComponentSnapshot(
    string Ref,
    string Component,
    string Label,
    AgentValue? Value,
    IReadOnlyList<string> Actions,
    bool Disabled = false,
    bool OffPage = false,
    bool ConfirmRequired = false,
    IReadOnlyList<string>? ConfirmActions = null);

/// <summary>One selectable option of a select, filter or view list, as returned by <c>AgentBridgeService.Query</c>.</summary>
/// <param name="Value">The value to pass as the argument of <c>setValue</c> or <c>toggle</c>.</param>
/// <param name="Label">The text shown to the user.</param>
/// <param name="Selected">Whether the option is currently selected.</param>
/// <param name="Disabled">Whether the option cannot be chosen.</param>
public sealed record AgentChoiceItem(
    string Value,
    string Label,
    bool Selected = false,
    bool Disabled = false);

/// <summary>A component matched by <c>AgentBridgeService.Query</c>, with the choices that matched.</summary>
/// <param name="Ref">The stable reference of the component.</param>
/// <param name="Component">The component kind.</param>
/// <param name="Label">The component label.</param>
/// <param name="Items">At most 50 matching choices.</param>
/// <param name="More">How many matching choices were left out because of the 50 item limit.</param>
/// <param name="Records">One page of the collection named by <see cref="AgentQueryRequest.Collection"/>: <see cref="AgentRowRecord"/>, <see cref="AgentIssueRecord"/>, <see cref="AgentColumnRecord"/>, <see cref="AgentHeaderRecord"/> or <see cref="AgentQuestionRecord"/>, by collection. <see langword="null"/> when no collection was requested.</param>
/// <param name="Total">How many records the collection holds after filtering, so a caller knows whether to ask for the next page. 0 when <paramref name="Records"/> is <see langword="null"/>.</param>
/// <param name="Offset">The index of the first record of <paramref name="Records"/> in the filtered collection.</param>
public sealed record AgentQueryMatch(
    string Ref,
    string Component,
    string Label,
    IReadOnlyList<AgentChoiceItem> Items,
    int More = 0,
    IReadOnlyList<AgentRecord>? Records = null,
    int Total = 0,
    int Offset = 0);

/// <summary>
/// Filters for <c>AgentBridgeService.Query</c>. All text comparisons ignore case, accents and repeated whitespace.
/// </summary>
public sealed class AgentQueryRequest
{
    /// <summary>Creates a request with no filters, which matches every component.</summary>
    public AgentQueryRequest()
    {
    }

    /// <summary>
    /// A component reference or label. When set, the query only inspects that component's choices and the
    /// other filters apply to those choices. A <c>Within</c> value that matches nothing returns an empty list.
    /// </summary>
    public string? Within { get; init; }

    /// <summary>Text that must appear in a component label, or in the label or value of one of its choices.</summary>
    public string? Text { get; init; }

    /// <summary>Restricts the result to one component kind, for example <c>select</c>. Ignored when <see cref="Within"/> is set.</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// With <see cref="Within"/>, pages through a collection of records the component holds instead of listing its
    /// choices. A spreadsheet has <c>rows</c>, <c>issues</c>, <c>columns</c>, <c>headers</c> and <c>questions</c>.
    /// An unknown collection returns an empty match.
    /// </summary>
    public string? Collection { get; init; }

    /// <summary>The index of the first record to return from <see cref="Collection"/>. Negative values count as 0.</summary>
    public int Offset { get; init; }

    /// <summary>The number of records to return from <see cref="Collection"/>. 25 when 0 or negative, and never more than 100.</summary>
    public int Limit { get; init; }

    /// <summary>
    /// Narrows <see cref="Collection"/> to one subset. For spreadsheet <c>rows</c>: <c>importable</c>, <c>invalid</c>,
    /// <c>discarded</c> or <c>all</c>. For <c>issues</c>: <c>blocking</c>, <c>warnings</c> or <c>all</c>. For
    /// <c>questions</c>: <c>open</c> or <c>all</c>. <see cref="Text"/> also filters records by their text.
    /// </summary>
    public string? Filter { get; init; }
}

/// <summary>
/// What an agent needs to know about a spreadsheet import to decide the next action, without the rows. It is the
/// <see cref="AgentComponentSnapshot.Value"/> of a spreadsheet component, so it is an <see cref="AgentValue"/>; page through rows and issues with
/// <c>AgentBridgeService.Query</c>.
/// </summary>
/// <param name="Step">The wizard step in progress: <c>file</c>, <c>columns</c>, <c>values</c>, <c>review</c> or <c>submit</c>.</param>
/// <param name="FileLoaded">Whether a file is loaded.</param>
/// <param name="FileName">The name of the loaded file, or <see langword="null"/>.</param>
/// <param name="Rows">The number of data rows in the file.</param>
/// <param name="ImportableRows">The rows that would be imported.</param>
/// <param name="InvalidRows">The rows that have an error and are not discarded.</param>
/// <param name="DiscardedRows">The rows left out of the import, whatever the reason.</param>
/// <param name="RowsWithWarnings">The importable rows that carry a warning.</param>
/// <param name="OpenQuestions">The unknown values that still need an answer.</param>
/// <param name="UnmappedRequiredColumns">The labels of the required columns no file column is assigned to.</param>
/// <param name="CanSubmit">Whether a submit would be accepted now.</param>
/// <param name="SubmitStatus">The submit state: <c>Idle</c>, <c>Running</c>, <c>Done</c> or <c>Error</c>.</param>
/// <param name="ImportedCount">The number of rows the last submit sent.</param>
/// <param name="SubmitError">The message of the failed submit, or <see langword="null"/>.</param>
public sealed record AgentSpreadsheetSummary(
    string Step,
    bool FileLoaded,
    string? FileName,
    int Rows,
    int ImportableRows,
    int InvalidRows,
    int DiscardedRows,
    int RowsWithWarnings,
    int OpenQuestions,
    IReadOnlyList<string> UnmappedRequiredColumns,
    bool CanSubmit,
    string SubmitStatus,
    int ImportedCount,
    string? SubmitError) : AgentValue;

/// <summary>One action to run in an <c>AgentBridgeService.ActAsync</c> call.</summary>
/// <param name="Ref">A component reference such as <c>c2</c>, or a component label.</param>
/// <param name="Action">The action name, which must be one of the component's <see cref="AgentComponentSnapshot.Actions"/>.</param>
/// <param name="Args">The action arguments, for example the new value for <c>setValue</c>. Plain values convert to <see cref="AgentArgument"/>, so <c>["Klyroo"]</c> and <c>[12]</c> work. <see langword="null"/> means no arguments.</param>
public sealed record AgentActStep(
    string Ref,
    string Action,
    IReadOnlyList<AgentArgument>? Args = null);

/// <summary>The outcome of one step that ran successfully, listed in <see cref="AgentActResult.Steps"/>.</summary>
/// <param name="Ref">The reference of the component the step ran on.</param>
/// <param name="Action">The action that ran.</param>
/// <param name="Ok">Always <see langword="true"/> for steps the bridge lists.</param>
/// <param name="Value">The component value after the step, or the text <c>"[redacted]"</c> for a sensitive field. <see langword="null"/> when the component has no value.</param>
public sealed record AgentActStepOutcome(
    string Ref,
    string Action,
    bool Ok,
    AgentValue? Value);

/// <summary>
/// The result of <c>AgentBridgeService.ActAsync</c>. Steps run in order and the first failure stops the run, so
/// <paramref name="Steps"/> only lists the steps that succeeded before it. Earlier steps are not rolled back.
/// </summary>
/// <param name="Ok">Whether every step succeeded.</param>
/// <param name="Steps">The steps that succeeded.</param>
/// <param name="Values">The latest value of each component the run touched, keyed by reference.</param>
/// <param name="Error">A message naming the failing step and the reason, or <see langword="null"/> on success.</param>
public sealed record AgentActResult(
    bool Ok,
    IReadOnlyList<AgentActStepOutcome> Steps,
    IReadOnlyDictionary<string, AgentValue?> Values,
    string? Error = null) : AgentToolResult;

/// <summary>
/// Describes one agent tool (<c>snapshot</c>, <c>query</c> or <c>act</c>) so you can expose it to an LLM or an MCP
/// server. Returned by <c>AgentBridgeService.GetToolDefinitions</c>.
/// </summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">What the tool does, written for a language model.</param>
/// <param name="ReadOnlyHint">Whether the tool never changes state.</param>
/// <param name="ConsequentialHint">Whether the tool can have effects that are hard to undo.</param>
/// <param name="UntrustedContentHint">Whether the tool result contains text that comes from the UI and must not be treated as instructions.</param>
/// <param name="InputSchema">The JSON Schema of the tool arguments, an object schema with the same shape as the <c>inputSchema</c> of the tools in <c>klyroo-agent.js</c>, ready to hand to an LLM or MCP host.</param>
public sealed record AgentToolDefinition(
    string Name,
    string Description,
    bool ReadOnlyHint,
    bool ConsequentialHint,
    bool UntrustedContentHint,
    System.Text.Json.JsonElement InputSchema);

/// <summary>
/// The result of a tool call that could not run, for example because the arguments were malformed or the tool is
/// unknown. Returned by <c>AgentBridgeService.InvokeToolAsync</c> instead of an exception, so the model can read
/// the message and correct the call.
/// </summary>
/// <param name="Error">What was wrong with the call.</param>
public sealed record AgentToolError(string Error) : AgentToolResult;

/// <summary>
/// A bounded undo and redo history of immutable snapshots. It is independent of the agent bridge: use it for any
/// state you want to step back and forth through. It is not thread safe.
/// </summary>
/// <typeparam name="T">The snapshot type. Snapshots are compared with the default equality comparer, so use a value or record type.</typeparam>
public sealed class UndoStack<T>
{
    private readonly List<T> _past = [];
    private readonly List<T> _future = [];
    private readonly int _limit;

    /// <summary>Creates a stack whose current snapshot is <paramref name="initial"/>.</summary>
    /// <param name="initial">The starting snapshot.</param>
    /// <param name="limit">The maximum number of undo steps kept. Values below 1 are treated as 1; the oldest step is dropped first.</param>
    public UndoStack(T initial, int limit = 50)
    {
        Current = initial;
        _limit = Math.Max(1, limit);
    }

    /// <summary>The snapshot the history currently points at.</summary>
    public T Current { get; private set; }

    /// <summary>Whether <see cref="Undo"/> would move to an earlier snapshot.</summary>
    public bool CanUndo => _past.Count > 0;

    /// <summary>Whether <see cref="Redo"/> would move to a later snapshot.</summary>
    public bool CanRedo => _future.Count > 0;

    /// <summary>
    /// Makes <paramref name="next"/> the current snapshot and discards any redo history. Does nothing when
    /// <paramref name="next"/> equals <see cref="Current"/>.
    /// </summary>
    /// <param name="next">The new snapshot.</param>
    /// <returns>The current snapshot after the call.</returns>
    public T Record(T next)
    {
        if (EqualityComparer<T>.Default.Equals(Current, next)) return Current;
        _past.Add(Current);
        if (_past.Count > _limit) _past.RemoveAt(0);
        Current = next;
        _future.Clear();
        return Current;
    }

    /// <summary>Moves back one snapshot. Does nothing when <see cref="CanUndo"/> is <see langword="false"/>.</summary>
    /// <returns>The current snapshot after the call.</returns>
    public T Undo()
    {
        if (_past.Count == 0) return Current;
        _future.Add(Current);
        Current = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        return Current;
    }

    /// <summary>Moves forward one snapshot. Does nothing when <see cref="CanRedo"/> is <see langword="false"/>.</summary>
    /// <returns>The current snapshot after the call.</returns>
    public T Redo()
    {
        if (_future.Count == 0) return Current;
        _past.Add(Current);
        Current = _future[^1];
        _future.RemoveAt(_future.Count - 1);
        return Current;
    }

    /// <summary>Forgets the undo and redo history and keeps <see cref="Current"/>.</summary>
    public void Clear()
    {
        _past.Clear();
        _future.Clear();
    }
}
