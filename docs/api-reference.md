# Klyroo.Agent API reference

> Generated from the compiled assemblies and their XML documentation. Do not edit by hand.

## Klyroo.Agent.Core

Classes (2) · Records (10) · Enums (1)

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

`Klyroo.Agent` · record

The result of `AgentBridgeService.ActAsync`. Steps run in order and the first failure stops the run, so `Steps` only lists the steps that succeeded before it. Earlier steps are not rolled back.

| Constructor | Description |
| :--- | :--- |
| `AgentActResult(bool Ok, IReadOnlyList<AgentActStepOutcome> Steps, IReadOnlyDictionary<string, object?> Values, string? Error = null)` | The result of `AgentBridgeService.ActAsync`. Steps run in order and the first failure stops the run, so `Steps` only lists the steps that succeeded before it. Earlier steps are not rolled back. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Error` (init) | `string?` | A message naming the failing step and the reason, or `null` on success. |
| `Ok` (init) | `bool` | Whether every step succeeded. |
| `Steps` (init) | `IReadOnlyList<AgentActStepOutcome>` | The steps that succeeded. |
| `Values` (init) | `IReadOnlyDictionary<string, object?>` | The latest value of each component the run touched, keyed by reference. |

#### `AgentActStep`

`Klyroo.Agent` · record

One action to run in an `AgentBridgeService.ActAsync` call.

| Constructor | Description |
| :--- | :--- |
| `AgentActStep(string Ref, string Action, IReadOnlyList<object?>? Args = null)` | One action to run in an `AgentBridgeService.ActAsync` call. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Action` (init) | `string` | The action name, which must be one of the component's `AgentComponentSnapshot.Actions`. |
| `Args` (init) | `IReadOnlyList<object?>?` | The action arguments, for example the new value for `setValue`. `null` means no arguments. |
| `Ref` (init) | `string` | A component reference such as `c2`, or a component label. |

#### `AgentActStepOutcome`

`Klyroo.Agent` · record

The outcome of one step that ran successfully, listed in `AgentActResult.Steps`.

| Constructor | Description |
| :--- | :--- |
| `AgentActStepOutcome(string Ref, string Action, bool Ok, object? Value)` | The outcome of one step that ran successfully, listed in `AgentActResult.Steps`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Action` (init) | `string` | The action that ran. |
| `Ok` (init) | `bool` | Always `true` for steps the bridge lists. |
| `Ref` (init) | `string` | The reference of the component the step ran on. |
| `Value` (init) | `object?` | The component value after the step, or `"[redacted]"` for a sensitive field. |

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

#### `AgentComponentSnapshot`

`Klyroo.Agent` · record

One registered component as returned by `AgentBridgeService.Snapshot`.

| Constructor | Description |
| :--- | :--- |
| `AgentComponentSnapshot(string Ref, string Component, string Label, object? Value, IReadOnlyList<string> Actions, bool Disabled = false, bool OffPage = false, bool ConfirmRequired = false, IReadOnlyList<string>? ConfirmActions = null)` | One registered component as returned by `AgentBridgeService.Snapshot`. |

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
| `Value` (init) | `object?` | The current value, or `"[redacted]"` for a sensitive field. |

#### `AgentQueryMatch`

`Klyroo.Agent` · record

A component matched by `AgentBridgeService.Query`, with the choices that matched.

| Constructor | Description |
| :--- | :--- |
| `AgentQueryMatch(string Ref, string Component, string Label, IReadOnlyList<AgentChoiceItem> Items, int More = 0, IReadOnlyList<IReadOnlyDictionary<string, object?>>? Records = null, int Total = 0, int Offset = 0)` | A component matched by `AgentBridgeService.Query`, with the choices that matched. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Component` (init) | `string` | The component kind. |
| `Items` (init) | `IReadOnlyList<AgentChoiceItem>` | At most 50 matching choices. |
| `Label` (init) | `string` | The component label. |
| `More` (init) | `int` | How many matching choices were left out because of the 50 item limit. |
| `Offset` (init) | `int` | The index of the first record of `Records` in the filtered collection. |
| `Records` (init) | `IReadOnlyList<IReadOnlyDictionary<string, object?>>?` | One page of the collection named by `AgentQueryRequest.Collection`, each record a map of field names to values. `null` when no collection was requested. |
| `Ref` (init) | `string` | The stable reference of the component. |
| `Total` (init) | `int` | How many records the collection holds after filtering, so a caller knows whether to ask for the next page. 0 when `Records` is `null`. |

#### `AgentSpreadsheetSummary`

`Klyroo.Agent` · record

What an agent needs to know about a spreadsheet import to decide the next action, without the rows. It is the `AgentComponentSnapshot.Value` of a spreadsheet component; page through rows and issues with `AgentBridgeService.Query`.

| Constructor | Description |
| :--- | :--- |
| `AgentSpreadsheetSummary(string Step, bool FileLoaded, string? FileName, int Rows, int ImportableRows, int InvalidRows, int DiscardedRows, int RowsWithWarnings, int OpenQuestions, IReadOnlyList<string> UnmappedRequiredColumns, bool CanSubmit, string SubmitStatus, int ImportedCount, string? SubmitError)` | What an agent needs to know about a spreadsheet import to decide the next action, without the rows. It is the `AgentComponentSnapshot.Value` of a spreadsheet component; page through rows and issues with `AgentBridgeService.Query`. |

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

`Klyroo.Agent` · record

The result of a tool call that could not run, for example because the arguments were malformed or the tool is unknown. Returned by `AgentBridgeService.InvokeToolAsync` instead of an exception, so the model can read the message and correct the call.

| Constructor | Description |
| :--- | :--- |
| `AgentToolError(string Error)` | The result of a tool call that could not run, for example because the arguments were malformed or the tool is unknown. Returned by `AgentBridgeService.InvokeToolAsync` instead of an exception, so the model can read the message and correct the call. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Error` (init) | `string` | What was wrong with the call. |

#### `ObservedChange`

`Klyroo.Agent` · record

A notification delivered to subscribers of `AgentBridgeService.Observe` after an action step succeeds. For password and one-time-code fields, `Previous` and `Value` are replaced by `"[redacted]"`.

| Constructor | Description |
| :--- | :--- |
| `ObservedChange(string Component, string Ref, string Label, string Key, object? Previous, object? Value, string Reason, AgentChangeSource Source, string? Row = null)` | A notification delivered to subscribers of `AgentBridgeService.Observe` after an action step succeeds. For password and one-time-code fields, `Previous` and `Value` are replaced by `"[redacted]"`. |

| Property | Type | Description |
| :--- | :--- | :--- |
| `Component` (init) | `string` | The component kind, for example `input`, `select`, `form` or `dashboard`. |
| `Key` (init) | `string` | The property that changed. The bridge always reports `value`. |
| `Label` (init) | `string` | The label the component was registered with. |
| `Previous` (init) | `object?` | The value before the step ran. |
| `Reason` (init) | `string` | The action name that caused the change, for example `setValue`. |
| `Ref` (init) | `string` | The stable reference of the component, for example `c3`. |
| `Row` (init) | `string?` | The row a step addressed on a tabular component: the user-visible row number of a spreadsheet `editCell` step, the row numbers of `discardRows` and `restoreRows` joined by commas, otherwise `null`. |
| `Source` (init) | `AgentChangeSource` | What caused the change. |
| `Value` (init) | `object?` | The value after the step ran. |

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

| Method | Returns | Description |
| :--- | :--- | :--- |
| `ActAsync(IReadOnlyList<AgentActStep> steps, Func<AgentActStep, bool, Task<bool>>? allow = null)` | `Task<AgentActResult>` | Runs the steps in order. A step is resolved by reference or by label (case and accent insensitive), then checked for existence, ambiguity, disabled state, visibility (a component that is not displayed, such as a conditional field whose condition is false, is refused) and supported action, then passed to `AgentBridgeService.AllowPolicy` and `allow`. The first failing step stops the run; steps that already ran are not rolled back. Each successful step raises one `ObservedChange`. Failures, including an exception thrown while the action runs, are reported in `AgentActResult.Error` rather than thrown. |
| `static GetToolDefinitions()` | `IReadOnlyList<AgentToolDefinition>` | Returns the definitions of the three agent tools, `snapshot`, `query` and `act`, for registration with an LLM or MCP host, each with the JSON Schema of its arguments. |
| `InvokeToolAsync(string tool, JsonElement arguments, Func<AgentActStep, bool, Task<bool>>? allow = null)` | `Task<object>` | Runs one tool call the way an LLM or MCP host delivers it: a tool name and the raw JSON object of its arguments. Arguments are read as JSON values, so numbers, booleans and lists in `args` keep their type, and a malformed call is reported as an `AgentToolError` rather than thrown, since the model wrote it. |
| `Observe(Action<ObservedChange> callback)` | `IDisposable` | Subscribes to the changes made through `AgentBridgeService.ActAsync`. The callback runs synchronously after each successful step. Changes a user makes directly in the UI are not reported. An exception thrown by the callback is ignored: the step is already applied, so the run goes on and the other subscribers are still called. |
| `Query(AgentQueryRequest? request = null)` | `IReadOnlyList<AgentQueryMatch>` | Finds components by label, kind or choice text, or lists the choices of one component, without opening any popup. Matching ignores case and accents. |
| `RegisterDashboard(DashboardController dashboard)` | `IDisposable` | Registers a dashboard. Each non-headless filter becomes a `select` component, and the dashboard itself accepts `setView`, `resetFilters` and `refresh`. On a filter, `setValue` replaces the selection with the given value, or with every value of a list for a multiple filter; `toggle` adds or removes one value of a multiple filter; `clear` (multiple filters only) empties the selection; and `reset` restores the default values. A value that is not one of the filter's options fails the step. |
| `RegisterForm(FormController form, bool confirmSubmit = true)` | `IDisposable` | Registers every stateful field of a form, plus the form itself. Fields become `input`, `select`, `checkbox` or `number` components; the form accepts `submit`, `reset` and, for stepped forms, `nextStep` and `previousStep`. Password and one-time-code fields are always redacted. Hidden fields are never exposed. A field that is not in effect, because of its condition, a hidden container, section or tab, or a skipped step (the rule `FormController.ActiveFields` uses for the output), is left out of `AgentBridgeService.Snapshot` and `AgentBridgeService.Query` and refuses every step. A field of another step that is in effect is listed only when `AgentBridgeService.Snapshot` is asked for off-page components. Registering the same form twice creates duplicate labels, which `AgentBridgeService.ActAsync` rejects as ambiguous. |
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
