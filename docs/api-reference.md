# Klyroo.Agent API reference

> Generated from the compiled assemblies and their XML documentation. Do not edit by hand.

## Klyroo.Agent.Core

Classes (2) · Records (25) · Structs (1) · Enums (1)

### Classes

#### `AgentQueryRequest`

`Klyroo.Agent` · class

Filters for `AgentBridgeService.Query`. All text comparisons ignore case, accents and repeated whitespace.

| Constructor | Description |
| :--- | :--- |
| `AgentQueryRequest()` | Creates a request with no filters, which matches every component. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Collection` (init) | `string?` | With `AgentQueryRequest.Within`, pages through a collection of records the component holds instead of listing its choices. A spreadsheet has `rows`, `issues`, `columns`, `headers` and `questions`. An unknown collection returns an empty match. |
| `Filter` (init) | `string?` | Narrows `AgentQueryRequest.Collection` to one subset. For spreadsheet `rows`: `importable`, `invalid`, `discarded` or `all`. For `issues`: `blocking`, `warnings` or `all`. For `questions`: `open` or `all`. `AgentQueryRequest.Text` also filters records by their text. |
| `Kind` (init) | `string?` | Restricts the result to one component kind, for example `select`. Ignored when `AgentQueryRequest.Within` is set. |
| `Limit` (init) | `int` | The number of records to return from `AgentQueryRequest.Collection`. 25 when 0 or negative, and never more than 100. |
| `Offset` (init) | `int` | The index of the first record to return from `AgentQueryRequest.Collection`. Negative values count as 0. |
| `Text` (init) | `string?` | Text that must appear in a component label, or in the label or value of one of its choices. |
| `Within` (init) | `string?` | A component reference or label. When set, the query only inspects that component's choices and the other filters apply to those choices. A `Within` value that matches nothing returns an empty list. |

#### `UndoStack<T>`

`Klyroo.Agent` · class

A bounded undo and redo history of immutable snapshots. It is independent of the agent bridge: use it for any state you want to step back and forth through. It is not thread safe.

| Constructor | Description |
| :--- | :--- |
| `UndoStack<T>(T initial, int limit = 50)` | Creates a stack whose current snapshot is `initial`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `CanRedo` | `bool` | Whether `UndoStack.Redo` would move to a later snapshot. |
| `CanUndo` | `bool` | Whether `UndoStack.Undo` would move to an earlier snapshot. |
| `Current` | `T` | The snapshot the history currently points at. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `Clear()` | `void` | Forgets the undo and redo history and keeps `UndoStack.Current`. |
| `Record(T next)` | `T` | Makes `next` the current snapshot and discards any redo history. Does nothing when `next` equals `UndoStack.Current`. |
| `Redo()` | `T` | Moves forward one snapshot. Does nothing when `UndoStack.CanRedo` is `false`. |
| `Undo()` | `T` | Moves back one snapshot. Does nothing when `UndoStack.CanUndo` is `false`. |

### Records

#### `AgentActResult`

`Klyroo.Agent` · record · inherits `AgentToolResult`

The result of `AgentBridgeService.ActAsync`. Steps run in order and the first failure stops the run, so `Steps` only lists the steps that succeeded before it. Earlier steps are not rolled back.

| Constructor | Description |
| :--- | :--- |
| `AgentActResult(bool Ok, IReadOnlyList<AgentActStepOutcome> Steps, IReadOnlyDictionary<string, AgentValue?> Values, string? Error = null)` | The result of `AgentBridgeService.ActAsync`. Steps run in order and the first failure stops the run, so `Steps` only lists the steps that succeeded before it. Earlier steps are not rolled back. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Error` (init) | `string?` | A message naming the failing step and the reason, or `null` on success. |
| `Ok` (init) | `bool` | Whether every step succeeded. |
| `Steps` (init) | `IReadOnlyList<AgentActStepOutcome>` | The steps that succeeded. |
| `Values` (init) | `IReadOnlyDictionary<string, AgentValue?>` | The latest value of each component the run touched, keyed by reference. |

#### `AgentActStep`

`Klyroo.Agent` · record

One action to run in an `AgentBridgeService.ActAsync` call.

| Constructor | Description |
| :--- | :--- |
| `AgentActStep(string Ref, string Action, IReadOnlyList<AgentArgument>? Args = null)` | One action to run in an `AgentBridgeService.ActAsync` call. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Action` (init) | `string` | The action name, which must be one of the component's `AgentComponentSnapshot.Actions`. |
| `Args` (init) | `IReadOnlyList<AgentArgument>?` | The action arguments, for example the new value for `setValue`. Plain values convert to `AgentArgument`, so `["Klyroo"]` and `[12]` work. `null` means no arguments. |
| `Ref` (init) | `string` | A component reference such as `c2`, or a component label. |

#### `AgentActStepOutcome`

`Klyroo.Agent` · record

The outcome of one step that ran successfully, listed in `AgentActResult.Steps`.

| Constructor | Description |
| :--- | :--- |
| `AgentActStepOutcome(string Ref, string Action, bool Ok, AgentValue? Value)` | The outcome of one step that ran successfully, listed in `AgentActResult.Steps`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Action` (init) | `string` | The action that ran. |
| `Ok` (init) | `bool` | Always `true` for steps the bridge lists. |
| `Ref` (init) | `string` | The reference of the component the step ran on. |
| `Value` (init) | `AgentValue?` | The component value after the step, or the text `"[redacted]"` for a sensitive field. `null` when the component has no value. |

#### `AgentBooleanValue`

`Klyroo.Agent` · record · inherits `AgentValue`

A boolean value.

| Constructor | Description |
| :--- | :--- |
| `AgentBooleanValue(bool Flag)` | A boolean value. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Flag` (init) | `bool` | The flag. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `ToDisplayString()` | `string` | A short text for a log or a label: the text, the digits, `true` or `false`, or the items joined by a comma. |

#### `AgentChoiceItem`

`Klyroo.Agent` · record

One selectable option of a select, filter or view list, as returned by `AgentBridgeService.Query`.

| Constructor | Description |
| :--- | :--- |
| `AgentChoiceItem(string Value, string Label, bool Selected = false, bool Disabled = false)` | One selectable option of a select, filter or view list, as returned by `AgentBridgeService.Query`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Disabled` (init) | `bool` | Whether the option cannot be chosen. |
| `Label` (init) | `string` | The text shown to the user. |
| `Selected` (init) | `bool` | Whether the option is currently selected. |
| `Value` (init) | `string` | The value to pass as the argument of `setValue` or `toggle`. |

#### `AgentColumnRecord`

`Klyroo.Agent` · record · inherits `AgentRecord`

A column of a spreadsheet import, from the `columns` collection.

| Constructor | Description |
| :--- | :--- |
| `AgentColumnRecord(string Key, string Label, bool Required, bool Editable, string Status, int? HeaderIndex, string? Header, string? SuggestedHeader, int? SuggestedHeaderIndex, IReadOnlyList<string> Samples)` | A column of a spreadsheet import, from the `columns` collection. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Editable` (init) | `bool` | Whether `editCell` may change it. |
| `Header` (init) | `string?` | The text of that file column, or `null`. |
| `HeaderIndex` (init) | `int?` | The 0-based index of the file column assigned to it, or `null`. |
| `Key` (init) | `string` | The column key. |
| `Label` (init) | `string` | The label shown to the user. |
| `Required` (init) | `bool` | Whether the column must have a value. |
| `Samples` (init) | `IReadOnlyList<string>` | A few values found in the column. |
| `Status` (init) | `string` | The mapping status: `matched`, `suggested`, `unmatched`, `ignored` or another status of the importer. |
| `SuggestedHeader` (init) | `string?` | The file column the importer suggests, or `null`. |
| `SuggestedHeaderIndex` (init) | `int?` | The index of the suggested file column, or `null`. |

#### `AgentComponentSnapshot`

`Klyroo.Agent` · record

One registered component as returned by `AgentBridgeService.Snapshot`.

| Constructor | Description |
| :--- | :--- |
| `AgentComponentSnapshot(string Ref, string Component, string Label, AgentValue? Value, IReadOnlyList<string> Actions, bool Disabled = false, bool OffPage = false, bool ConfirmRequired = false, IReadOnlyList<string>? ConfirmActions = null)` | One registered component as returned by `AgentBridgeService.Snapshot`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Actions` (init) | `IReadOnlyList<string>` | The action names the component accepts in `AgentActStep.Action`. |
| `Component` (init) | `string` | The component kind: `input`, `select`, `checkbox`, `number`, `form`, `dashboard` or `spreadsheet`. |
| `ConfirmActions` (init) | `IReadOnlyList<string>?` | The actions that need confirmation whatever the component flag says, for example `submit` and `clearFile` on a spreadsheet. Empty when the component has none. |
| `ConfirmRequired` (init) | `bool` | Whether every action on the component needs confirmation, because it was registered that way. The bridge passes the flag to `AllowPolicy`. |
| `Disabled` (init) | `bool` | Whether the component currently rejects actions. |
| `Label` (init) | `string` | The human-readable label an agent can also use to address the component. |
| `OffPage` (init) | `bool` | Whether the component is not rendered right now, for example a conditional form field whose condition is false. |
| `Ref` (init) | `string` | The stable reference (`c1`, `c2`, ...) used to address the component in an action step. |
| `Value` (init) | `AgentValue?` | The current value, or the text `"[redacted]"` for a sensitive field. `null` when the component has no value, such as an empty field. |

#### `AgentHeaderRecord`

`Klyroo.Agent` · record · inherits `AgentRecord`

A column of the loaded file, from the `headers` collection.

| Constructor | Description |
| :--- | :--- |
| `AgentHeaderRecord(int Index, string Header, string? UsedBy)` | A column of the loaded file, from the `headers` collection. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Header` (init) | `string` | The header text. |
| `Index` (init) | `int` | The 0-based index of the file column, to pass to `assignColumn`. |
| `UsedBy` (init) | `string?` | The key of the column that uses it, or `null`. |

#### `AgentIssueRecord`

`Klyroo.Agent` · record · inherits `AgentRecord`

An issue of a spreadsheet import, from the `issues` collection or the `AgentRowRecord.Issues` of a row.

| Constructor | Description |
| :--- | :--- |
| `AgentIssueRecord(string? Field, string Code, string Level, string Message, int? RowNumber = null)` | An issue of a spreadsheet import, from the `issues` collection or the `AgentRowRecord.Issues` of a row. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Code` (init) | `string` | The stable code of the issue, such as `number` or `value.unknown`. |
| `Field` (init) | `string?` | The key of the column the issue is about, or `null` for a row issue. |
| `Level` (init) | `string` | The level: `error` or `warning`. |
| `Message` (init) | `string` | What is wrong, in words. |
| `RowNumber` (init) | `int?` | The row number the issue belongs to. Set in the `issues` collection, `null` inside a row. |

#### `AgentListValue`

`Klyroo.Agent` · record · inherits `AgentValue`

A list of values, such as the selected options of a multiple choice.

| Constructor | Description |
| :--- | :--- |
| `AgentListValue(IReadOnlyList<AgentValue> Items)` | A list of values, such as the selected options of a multiple choice. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Items` (init) | `IReadOnlyList<AgentValue>` | The items, in order. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `ToDisplayString()` | `string` | A short text for a log or a label: the text, the digits, `true` or `false`, or the items joined by a comma. |

#### `AgentNumberValue`

`Klyroo.Agent` · record · inherits `AgentValue`

A number, with the digits exactly as the component reports them, so a 64-bit integer or a decimal of 28 digits is not rounded.

| Constructor | Description |
| :--- | :--- |
| `AgentNumberValue(string Text)` | A number, with the digits exactly as the component reports them, so a 64-bit integer or a decimal of 28 digits is not rounded. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Approximate` | `double?` | The number as a `Double`, or `null` when it is out of range. It can lose digits; read `AgentNumberValue.Text` for the exact number. |
| `Exact` | `decimal?` | The number as a `Decimal`, or `null` when it does not fit one. |
| `Text` (init) | `string` | The number in the invariant format of JSON, such as `12.5` or `9007199254740993`. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `ToDisplayString()` | `string` | A short text for a log or a label: the text, the digits, `true` or `false`, or the items joined by a comma. |

#### `AgentQueryMatch`

`Klyroo.Agent` · record

A component matched by `AgentBridgeService.Query`, with the choices that matched.

| Constructor | Description |
| :--- | :--- |
| `AgentQueryMatch(string Ref, string Component, string Label, IReadOnlyList<AgentChoiceItem> Items, int More = 0, IReadOnlyList<AgentRecord>? Records = null, int Total = 0, int Offset = 0)` | A component matched by `AgentBridgeService.Query`, with the choices that matched. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Component` (init) | `string` | The component kind. |
| `Items` (init) | `IReadOnlyList<AgentChoiceItem>` | At most 50 matching choices. |
| `Label` (init) | `string` | The component label. |
| `More` (init) | `int` | How many matching choices were left out because of the 50 item limit. |
| `Offset` (init) | `int` | The index of the first record of `Records` in the filtered collection. |
| `Records` (init) | `IReadOnlyList<AgentRecord>?` | One page of the collection named by `AgentQueryRequest.Collection`: `AgentRowRecord`, `AgentIssueRecord`, `AgentColumnRecord`, `AgentHeaderRecord` or `AgentQuestionRecord`, by collection. `null` when no collection was requested. |
| `Ref` (init) | `string` | The stable reference of the component. |
| `Total` (init) | `int` | How many records the collection holds after filtering, so a caller knows whether to ask for the next page. 0 when `Records` is `null`. |

#### `AgentQueryResult`

`Klyroo.Agent` · record · inherits `AgentToolResult`

The result of the `query` tool.

| Constructor | Description |
| :--- | :--- |
| `AgentQueryResult(IReadOnlyList<AgentQueryMatch> Matches)` | The result of the `query` tool. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Matches` (init) | `IReadOnlyList<AgentQueryMatch>` | The matching components, as `AgentBridgeService.Query` returns them. |

#### `AgentQuestionChoice`

`Klyroo.Agent` · record

One value an answer of an `AgentQuestionRecord` can map to.

| Constructor | Description |
| :--- | :--- |
| `AgentQuestionChoice(string Value, string Label)` | One value an answer of an `AgentQuestionRecord` can map to. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Label` (init) | `string` | The text shown to the user. |
| `Value` (init) | `string` | The value to pass to `answerValue`. |

#### `AgentQuestionRecord`

`Klyroo.Agent` · record · inherits `AgentRecord`

A question the importer asks about an unknown value, from the `questions` collection.

| Constructor | Description |
| :--- | :--- |
| `AgentQuestionRecord(string Field, string FieldLabel, string RawValue, bool Open, string? Answer, string? MappedValue, bool CanCreate, int RowCount, IReadOnlyList<int> RowNumbers, IReadOnlyList<AgentQuestionChoice> Choices)` | A question the importer asks about an unknown value, from the `questions` collection. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Answer` (init) | `string?` | The answer given (`Map`, `LeaveEmpty`, `SkipRows` or `Create`), or `null`. |
| `CanCreate` (init) | `bool` | Whether a new value can be created for the column. |
| `Choices` (init) | `IReadOnlyList<AgentQuestionChoice>` | The values the answer can map to. |
| `Field` (init) | `string` | The key of the column the value is in. |
| `FieldLabel` (init) | `string` | The label of that column. |
| `MappedValue` (init) | `string?` | The value the answer maps to, or `null`. |
| `Open` (init) | `bool` | Whether the question still needs an answer. |
| `RawValue` (init) | `string` | The value as it appears in the file. |
| `RowCount` (init) | `int` | How many rows have the value. |
| `RowNumbers` (init) | `IReadOnlyList<int>` | The row numbers that have the value, at most 20. |

#### `AgentRecord`

`Klyroo.Agent` · record

One record of a collection a component pages through with `AgentQueryRequest.Collection`. The subtype depends on the collection: `AgentRowRecord` for `rows`, `AgentIssueRecord` for `issues`, `AgentColumnRecord` for `columns`, `AgentHeaderRecord` for `headers` and `AgentQuestionRecord` for `questions`. The JSON an agent reads has the properties of the subtype, in camel case.

#### `AgentRowRecord`

`Klyroo.Agent` · record · inherits `AgentRecord`

A data row of a spreadsheet import, from the `rows` collection.

| Constructor | Description |
| :--- | :--- |
| `AgentRowRecord(int RowNumber, string Status, bool Importable, string? DiscardReason, bool Edited, IReadOnlyDictionary<string, string> Cells, IReadOnlyList<AgentIssueRecord> Issues)` | A data row of a spreadsheet import, from the `rows` collection. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Cells` (init) | `IReadOnlyDictionary<string, string>` | The text of each cell, keyed by column key. |
| `DiscardReason` (init) | `string?` | Why the row is left out of the import, or `null`. |
| `Edited` (init) | `bool` | Whether a cell of the row was edited. |
| `Importable` (init) | `bool` | Whether the row would be imported. |
| `Issues` (init) | `IReadOnlyList<AgentIssueRecord>` | What is wrong with the row, empty when nothing is. |
| `RowNumber` (init) | `int` | The number the user sees for the row in the file. |
| `Status` (init) | `string` | The row status: `valid`, `warning` or `blocking`. |

#### `AgentSnapshotResult`

`Klyroo.Agent` · record · inherits `AgentToolResult`

The result of the `snapshot` tool.

| Constructor | Description |
| :--- | :--- |
| `AgentSnapshotResult(IReadOnlyList<AgentComponentSnapshot> Components)` | The result of the `snapshot` tool. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Components` (init) | `IReadOnlyList<AgentComponentSnapshot>` | The registered components, as `AgentBridgeService.Snapshot` returns them. |

#### `AgentSpreadsheetSummary`

`Klyroo.Agent` · record · inherits `AgentValue`

What an agent needs to know about a spreadsheet import to decide the next action, without the rows. It is the `AgentComponentSnapshot.Value` of a spreadsheet component, so it is an `AgentValue`; page through rows and issues with `AgentBridgeService.Query`.

| Constructor | Description |
| :--- | :--- |
| `AgentSpreadsheetSummary(string Step, bool FileLoaded, string? FileName, int Rows, int ImportableRows, int InvalidRows, int DiscardedRows, int RowsWithWarnings, int OpenQuestions, IReadOnlyList<string> UnmappedRequiredColumns, bool CanSubmit, string SubmitStatus, int ImportedCount, string? SubmitError)` | What an agent needs to know about a spreadsheet import to decide the next action, without the rows. It is the `AgentComponentSnapshot.Value` of a spreadsheet component, so it is an `AgentValue`; page through rows and issues with `AgentBridgeService.Query`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `CanSubmit` (init) | `bool` | Whether a submit would be accepted now. |
| `DiscardedRows` (init) | `int` | The rows left out of the import, whatever the reason. |
| `FileLoaded` (init) | `bool` | Whether a file is loaded. |
| `FileName` (init) | `string?` | The name of the loaded file, or `null`. |
| `ImportableRows` (init) | `int` | The rows that would be imported. |
| `ImportedCount` (init) | `int` | The number of rows the last submit sent. |
| `InvalidRows` (init) | `int` | The rows that have an error and are not discarded. |
| `OpenQuestions` (init) | `int` | The unknown values that still need an answer. |
| `Rows` (init) | `int` | The number of data rows in the file. |
| `RowsWithWarnings` (init) | `int` | The importable rows that carry a warning. |
| `Step` (init) | `string` | The wizard step in progress: `file`, `columns`, `values`, `review` or `submit`. |
| `SubmitError` (init) | `string?` | The message of the failed submit, or `null`. |
| `SubmitStatus` (init) | `string` | The submit state: `Idle`, `Running`, `Done` or `Error`. |
| `UnmappedRequiredColumns` (init) | `IReadOnlyList<string>` | The labels of the required columns no file column is assigned to. |

#### `AgentTextValue`

`Klyroo.Agent` · record · inherits `AgentValue`

A text value.

| Constructor | Description |
| :--- | :--- |
| `AgentTextValue(string Text)` | A text value. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Text` (init) | `string` | The text. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `ToDisplayString()` | `string` | A short text for a log or a label: the text, the digits, `true` or `false`, or the items joined by a comma. |

#### `AgentToolDefinition`

`Klyroo.Agent` · record

Describes one agent tool (`snapshot`, `query` or `act`) so you can expose it to an LLM or an MCP server. Returned by `AgentBridgeService.GetToolDefinitions`.

| Constructor | Description |
| :--- | :--- |
| `AgentToolDefinition(string Name, string Description, bool ReadOnlyHint, bool ConsequentialHint, bool UntrustedContentHint, JsonElement InputSchema)` | Describes one agent tool (`snapshot`, `query` or `act`) so you can expose it to an LLM or an MCP server. Returned by `AgentBridgeService.GetToolDefinitions`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `ConsequentialHint` (init) | `bool` | Whether the tool can have effects that are hard to undo. |
| `Description` (init) | `string` | What the tool does, written for a language model. |
| `InputSchema` (init) | `JsonElement` | The JSON Schema of the tool arguments, an object schema with the same shape as the `inputSchema` of the tools in `klyroo-agent.js`, ready to hand to an LLM or MCP host. |
| `Name` (init) | `string` | The tool name. |
| `ReadOnlyHint` (init) | `bool` | Whether the tool never changes state. |
| `UntrustedContentHint` (init) | `bool` | Whether the tool result contains text that comes from the UI and must not be treated as instructions. |

#### `AgentToolError`

`Klyroo.Agent` · record · inherits `AgentToolResult`

The result of a tool call that could not run, for example because the arguments were malformed or the tool is unknown. Returned by `AgentBridgeService.InvokeToolAsync` instead of an exception, so the model can read the message and correct the call.

| Constructor | Description |
| :--- | :--- |
| `AgentToolError(string Error)` | The result of a tool call that could not run, for example because the arguments were malformed or the tool is unknown. Returned by `AgentBridgeService.InvokeToolAsync` instead of an exception, so the model can read the message and correct the call. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Error` (init) | `string` | What was wrong with the call. |

#### `AgentToolResult`

`Klyroo.Agent` · record

What `AgentBridgeService.InvokeToolAsync` returns: one of `AgentSnapshotResult`, `AgentQueryResult`, `AgentActResult` or `AgentToolError`. Match on the subtype.

#### `AgentValue`

`Klyroo.Agent` · record

A value the agent bridge reports: the current value of a component, the value before and after a step, or the value a step left behind. It is a closed set, so a consumer matches on the subtype instead of testing a boxed value: `AgentTextValue`, `AgentNumberValue`, `AgentBooleanValue`, `AgentListValue` and `AgentSpreadsheetSummary`. No value at all is `null`. It serializes to the JSON an agent reads: a string, a number, a boolean, an array, or the summary object. Serialize it through the `AgentValue` type, as a property or with `Serialize<AgentValue>`, so the converter of this type is the one used.

| Method | Returns | Description |
| :--- | :--- | :--- |
| `static From(string text)` | `AgentValue` | Creates a text value. |
| `static From(bool flag)` | `AgentValue` | Creates a boolean value. |
| `static From(IEnumerable<string> items)` | `AgentValue` | Creates a list of text values. |
| `static FromJson(JsonElement element)` | `AgentValue?` | Reads a JSON value into an `AgentValue`. A number keeps its digits, a list keeps its order, and a JSON object, which no component holds, is kept as its JSON text. |
| `ToDisplayString()` | `string` | A short text for a log or a label: the text, the digits, `true` or `false`, or the items joined by a comma. |

#### `ObservedChange`

`Klyroo.Agent` · record

A notification delivered to subscribers of `AgentBridgeService.Observe` after an action step succeeds. For password and one-time-code fields, `Previous` and `Value` are replaced by `"[redacted]"`.

| Constructor | Description |
| :--- | :--- |
| `ObservedChange(string Component, string Ref, string Label, string Key, AgentValue? Previous, AgentValue? Value, string Reason, AgentChangeSource Source, string? Row = null)` | A notification delivered to subscribers of `AgentBridgeService.Observe` after an action step succeeds. For password and one-time-code fields, `Previous` and `Value` are replaced by `"[redacted]"`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Component` (init) | `string` | The component kind, for example `input`, `select`, `form` or `dashboard`. |
| `Key` (init) | `string` | The property that changed. The bridge always reports `value`. |
| `Label` (init) | `string` | The label the component was registered with. |
| `Previous` (init) | `AgentValue?` | The value before the step ran. |
| `Reason` (init) | `string` | The action name that caused the change, for example `setValue`. |
| `Ref` (init) | `string` | The stable reference of the component, for example `c3`. |
| `Row` (init) | `string?` | The row a step addressed on a tabular component: the user-visible row number of a spreadsheet `editCell` step, the row numbers of `discardRows` and `restoreRows` joined by commas, otherwise `null`. |
| `Source` (init) | `AgentChangeSource` | What caused the change. |
| `Value` (init) | `AgentValue?` | The value after the step ran. |

### Structs

#### `AgentArgument`

`Klyroo.Agent` · struct

One argument of an `AgentActStep`: text, a number, a boolean, `null` or a list of those, held as the JSON value a language model sent. A .NET caller writes it as a plain value, because the conversions below create the JSON: `new AgentActStep("Quantity", "setValue", [12])`. A number keeps its exact digits, so a 64-bit integer or a decimal reaches a field as the digits given. An object is not an argument the bridge accepts: `AgentBridgeService.ActAsync` fails the step.

| Constructor | Description |
| :--- | :--- |
| `AgentArgument(JsonElement json)` | Creates an argument from a JSON value, such as the elements of the `args` of an `act` call. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Json` | `JsonElement` | The JSON value. `null` when the argument was created without a value. |
| `static Null` | `AgentArgument` | The argument that stands for no value. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `static List(params AgentArgument[] items)` | `AgentArgument` | Creates a list argument. |
| `static List(IEnumerable<string> items)` | `AgentArgument` | Creates a list of text arguments, such as the selected values of a multiple choice. |
| `static List(IEnumerable<int> items)` | `AgentArgument` | Creates a list of whole numbers, such as row numbers. |

### Enums

#### `AgentChangeSource`

`Klyroo.Agent` · enum

Identifies what caused a state change reported in `ObservedChange.Source`. The .NET `AgentBridgeService` only raises changes caused by `ActAsync`, so it always reports `AgentChangeSource.Agent`; the other members let your own code classify changes it forwards through the same record type.

| Value | Description |
| :--- | :--- |
| `Keyboard` | The change came from keyboard input. |
| `Mouse` | The change came from a mouse interaction. |
| `Touch` | The change came from a touch interaction. |
| `Pen` | The change came from a pen interaction. |
| `Virtual` | The change came from an assistive technology or a synthetic click. |
| `Programmatic` | The change was made by application code rather than by a user or an agent. |
| `Agent` | The change was made by an agent through `AgentBridgeService.ActAsync`. |

## Klyroo.Agent

Static classes and extensions (1) · Classes (1)

### Static classes and extensions

#### `KlyrooAgentServiceCollectionExtensions`

`Klyroo.Agent` · service

Dependency injection registration for Klyroo.Agent.

| Method | Returns | Description |
| :--- | :--- | :--- |
| `static AddKlyrooAgent(IServiceCollection services)` | `IServiceCollection` | Registers `AgentBridgeService` as a scoped service, so each Blazor circuit, HTTP request or MAUI scope gets its own bridge. Does nothing if the service is already registered. |

### Classes

#### `AgentBridgeService`

`Klyroo.Agent` · class

Exposes Klyroo.Form, Klyroo.Dashboard and Klyroo.Spreadsheet controllers to an AI agent through three operations: `AgentBridgeService.Snapshot` lists what is registered, `AgentBridgeService.Query` lists the choices of a select or filter, and `AgentBridgeService.ActAsync` runs named actions. Register the controllers you want to expose, then call these methods from your tool handlers. The service works on the controllers, not on rendered markup, so it needs no JavaScript interop and behaves the same in every render mode. It is not thread safe: use one instance per user session, which is what `AddKlyrooAgent` provides.

| Constructor | Description |
| :--- | :--- |
| `AgentBridgeService()` | Creates an empty bridge with no registered components and no `AgentBridgeService.AllowPolicy`. |

| Field | Type | Description |
| :--- | :--- | :--- |
| `const MaxStepsPerAct = 50` | `int` | The most steps one `AgentBridgeService.ActAsync` call accepts. A longer list is refused before any step runs, so a runaway model cannot queue unbounded work. |
| `const RedactedValue = "[redacted]"` | `string` | The text that replaces the value of password and one-time-code fields in snapshots, results and observed changes. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `AllowPolicy` (get, set) | `Func<AgentActStep, bool, Task<bool>>?` | The gate called before every `AgentBridgeService.ActAsync` step with the step and whether it needs confirmation, because its component was registered as confirmation-required or the action is one that always is (`submit` and `clearFile` on a spreadsheet). Return `false` to refuse the step. |
| `Logger` (get, set) | `ILogger?` | Where the bridge logs an action that threw. The text of such an exception can hold a path or a connection string, so `AgentBridgeService.ActAsync` does not give it to the model: the model reads a generic message, and the exception goes here. `null` logs nothing. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `ActAsync(IReadOnlyList<AgentActStep> steps, Func<AgentActStep, bool, Task<bool>>? allow = null)` | `Task<AgentActResult>` | Runs the steps in order. A step is resolved by reference or by label (case and accent insensitive), then checked for existence, ambiguity, disabled state, visibility (a component that is not displayed, such as a conditional field whose condition is false, is refused), supported action and the component's own refusals (a write to a sensitive form field that was not opted in, a write to a field of another step), then passed to `AgentBridgeService.AllowPolicy` and `allow`. The first failing step stops the run; steps that already ran are not rolled back. Each successful step raises one `ObservedChange`. Failures, including an exception thrown while the action runs, are reported in `AgentActResult.Error` rather than thrown; the text of such an exception goes to `AgentBridgeService.Logger`, never to the model. |
| `static GetToolDefinitions()` | `IReadOnlyList<AgentToolDefinition>` | Returns the definitions of the three agent tools, `snapshot`, `query` and `act`, for registration with an LLM or MCP host, each with the JSON Schema of its arguments. |
| `InvokeToolAsync(string tool, JsonElement arguments, Func<AgentActStep, bool, Task<bool>>? allow = null)` | `Task<AgentToolResult>` | Runs one tool call the way an LLM or MCP host delivers it: a tool name and the raw JSON object of its arguments. Arguments are read as JSON values, so numbers, booleans and lists in `args` keep their type, and a malformed call is reported as an `AgentToolError` rather than thrown, since the model wrote it. |
| `Observe(Action<ObservedChange> callback)` | `IDisposable` | Subscribes to the changes made through `AgentBridgeService.ActAsync`. The callback runs synchronously after each successful step. Changes a user makes directly in the UI are not reported. An exception thrown by the callback is ignored: the step is already applied, so the run goes on and the other subscribers are still called. |
| `Query(AgentQueryRequest? request = null)` | `IReadOnlyList<AgentQueryMatch>` | Finds components by label, kind or choice text, or lists the choices of one component, without opening any popup. Matching ignores case and accents. |
| `RegisterDashboard(DashboardController dashboard)` | `IDisposable` | Registers a dashboard. Each non-headless filter becomes a `select` component, and the dashboard itself accepts `setView`, `resetFilters` and `refresh`. On a filter, `setValue` replaces the selection with the given value, or with every value of a list for a multiple filter; `toggle` adds or removes one value of a multiple filter; `clear` (multiple filters only) empties the selection; and `reset` restores the default values. A value that is not one of the filter's options fails the step. |
| `RegisterForm(IFormRuntime form, bool confirmSubmit = true, IReadOnlyCollection<string>? allowSensitiveWrites = null)` | `IDisposable` | Registers every stateful field of a form, plus the form itself. `form` is the form seen through `IFormRuntime`, so a typed `FormController<TModel>` and a `DynamicFormController` are exposed the same way. Fields become `input`, `select`, `checkbox` or `number` components; the form accepts `submit`, `reset` and, for stepped forms, `nextStep` and `previousStep`. Password and one-time-code fields (any `IFormFieldAccess.Sensitive` field) are never readable, and ActAsync refuses every write to them unless `allowSensitiveWrites` names the field. Hidden inputs are never exposed. A field that is not in effect, because of its condition, a hidden container, section or tab, or a skipped step (`IFormFieldAccess.IsHidden`), is left out of `AgentBridgeService.Snapshot` and `AgentBridgeService.Query` and refuses every step. A field that is disabled refuses every step too, because the form itself would write to it. A field of another step that is in effect is listed only when `AgentBridgeService.Snapshot` is asked for off-page components, and refuses every write too: the model moves to the step with `nextStep`, which validates the current step like a user would, then writes. Registering the same form twice creates duplicate labels, which `AgentBridgeService.ActAsync` rejects as ambiguous. |
| `RegisterSpreadsheet(SpreadsheetImporter importer)` | `IDisposable` | Registers an importer as one `spreadsheet` component. Its snapshot value is an `AgentSpreadsheetSummary` (step, row counts, open questions, unmapped required columns), and `AgentBridgeService.Query` with `AgentQueryRequest.Within` and `AgentQueryRequest.Collection` pages through `rows`, `issues`, `columns`, `headers` and `questions`. |
| `Snapshot(bool includeOffPage = true)` | `IReadOnlyList<AgentComponentSnapshot>` | Lists the registered components in registration order, with their current value and available actions. At most 100 are returned. |

## Klyroo.Agent.AI

Static classes and extensions (1) · Classes (1) · Enums (1)

### Static classes and extensions

#### `AgentBridgeAIExtensions`

`Klyroo.Agent.AI` · service

Exposes an `AgentBridgeService` to any `IChatClient` as Microsoft.Extensions.AI functions.

| Field | Type | Description |
| :--- | :--- | :--- |
| `const ActWithApprovalName = "act_with_approval"` | `string` | The name of the function that runs steps that need confirmation, in `AgentConfirmationMode.RequireApproval` mode. |

| Method | Returns | Description |
| :--- | :--- | :--- |
| `static AsAIFunctions(AgentBridgeService bridge, AgentAIFunctionOptions? options = null)` | `IReadOnlyList<AIFunction>` | Returns the `snapshot`, `query` and `act` tools of the bridge as `AIFunction` instances, with their descriptions and JSON schemas, ready for `ChatOptions.Tools`. By default it also returns `AgentBridgeAIExtensions.ActWithApprovalName`, which is wrapped in `ApprovalRequiredAIFunction`; see `AgentConfirmationMode`. |

### Classes

#### `AgentAIFunctionOptions`

`Klyroo.Agent.AI` · class

Options for `AgentBridgeAIExtensions.AsAIFunctions`.

| Constructor | Description |
| :--- | :--- |
| `AgentAIFunctionOptions()` | Creates options with `AgentConfirmationMode.RequireApproval`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Confirmation` (init) | `AgentConfirmationMode` | How actions that need confirmation are handled. The default is `AgentConfirmationMode.RequireApproval`. |

### Enums

#### `AgentConfirmationMode`

`Klyroo.Agent.AI` · enum

How the AI functions treat actions that need confirmation. See `AgentAIFunctionOptions.Confirmation`.

| Value | Description |
| :--- | :--- |
| `RequireApproval` | The `act` function refuses steps that need confirmation and tells the model to call `act_with_approval`, a second function wrapped in `ApprovalRequiredAIFunction`. A `FunctionInvokingChatClient` then asks its caller for approval before that function runs. |
| `UseBridgePolicy` | Only `snapshot`, `query` and `act` are returned, and `act` leaves the decision about steps that need confirmation to `AgentBridgeService.AllowPolicy`, which refuses them when it is not set. Use it when your own code already decides, for example a policy that asks the user in your UI. |
