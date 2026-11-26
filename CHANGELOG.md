# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `RegisterForm`, `RegisterDashboard` and `RegisterSpreadsheet` return a handle that removes the registered components when disposed.
- `ObservedChange.Row` holds the row number of a spreadsheet `editCell` step.
- A dashboard filter `setValue` accepts a list of values for a multiple filter.
- `Klyroo.Agent.AI`, a new package: `AgentBridgeService.AsAIFunctions()` returns `snapshot`, `query` and `act` as `Microsoft.Extensions.AI` `AIFunction` instances with their schemas. Steps that need confirmation are refused by `act` and run through `act_with_approval`, which is wrapped in `ApprovalRequiredAIFunction`, so a `FunctionInvokingChatClient` asks for approval first. `AgentConfirmationMode.UseBridgePolicy` leaves the decision to `AllowPolicy` instead. The guides show an in-page chat in the same Blazor circuit.
- `AgentToolDefinition.InputSchema`, the JSON Schema of the tool arguments, and `AgentBridgeService.InvokeToolAsync`, which runs a tool call from its raw JSON arguments and returns an `AgentToolError` for a malformed call.
- Spreadsheet imports: the snapshot value is an `AgentSpreadsheetSummary`, `Query` pages through `rows`, `issues`, `columns`, `headers` and `questions` (`Collection`, `Offset`, `Limit`, `Filter` on `AgentQueryRequest`; `Records`, `Total`, `Offset` on `AgentQueryMatch`), and the new actions `assignColumn`, `ignoreColumn`, `resetColumn`, `answerValue`, `discardRows` and `restoreRows` address rows by the row number the user sees.
- `AgentComponentSnapshot.ConfirmActions` lists the actions that need confirmation whatever the component flag says. A spreadsheet `submit` and `clearFile` are in it.
- A refused step explains why (`was rejected: ...`): a select value that is not an option, a number out of range, an unknown row number, a read-only column, the blockers of a `submit`.
- `klyroo-agent.js` snapshot items report `confirmRequired`, and `packages/agent` has a `node --test` suite that runs the runtime against a fake DOM.

### Changed

- **Breaking (preview):** the bridge is strongly typed and no public member is an `object` any more. It follows the typed `Klyroo.Form`, `Klyroo.Spreadsheet` and `Klyroo.Dashboard`.
  - `RegisterForm` takes an `IFormRuntime` instead of a `FormController`, so a typed `FormController<TModel>` and a `DynamicFormController` are exposed the same way (the old `FormController` is `DynamicFormController`, the old `FormSchema` is `DynamicFormSchema`). The coercion of what a model sends moved out of the bridge: `IFormFieldAccess.TrySet(JsonElement)` of `Klyroo.Form` reads the JSON with the codec of the field (the internal `FormValueCoercion` and its number helpers are gone), so a `long` or `decimal` member keeps every digit and a value outside the type of the member is refused with a reason. The bridge still refuses a disabled, hidden or sensitive field itself, since the form writes to a disabled field when asked.
  - `AgentValue` (`AgentTextValue`, `AgentNumberValue`, `AgentBooleanValue`, `AgentListValue` and `AgentSpreadsheetSummary`) replaces `object?` in `AgentComponentSnapshot.Value`, `AgentActStepOutcome.Value`, `AgentActResult.Values` and `ObservedChange.Previous` and `Value`. `null` is no value. It serializes to the JSON the `object?` produced.
  - `AgentActStep.Args` is `IReadOnlyList<AgentArgument>?`. `AgentArgument` is a struct over the JSON value with implicit conversions from `string`, `bool`, `int`, `long`, `double`, `decimal`, `string[]` and `JsonElement`, so `["Atlas"]` and `[12]` still compile; `AgentArgument.List(...)` builds a list and a number keeps its digits. Internally the actions read `IReadOnlyList<JsonElement>`, and a step whose argument holds an object fails with `An object is not a valid value`. A `double` that JSON cannot hold (`NaN`, infinity) becomes text, which a numeric field refuses.
  - `AgentQueryMatch.Records` is `IReadOnlyList<AgentRecord>?`: `AgentRowRecord`, `AgentIssueRecord`, `AgentColumnRecord`, `AgentHeaderRecord` and `AgentQuestionRecord` (with `AgentQuestionChoice`) replace the dictionaries. Their JSON is the same, null properties included.
  - `AgentBridgeService.InvokeToolAsync` returns `Task<AgentToolResult>`: `AgentSnapshotResult` (`Components`), `AgentQueryResult` (`Matches`), `AgentActResult` or `AgentToolError`. `Klyroo.Agent.AI` writes the same JSON as before: a bare array for a snapshot and a query, the record for the others (`JsonCompatibilityTests` compares 28 tool calls with text written by the untyped bridge).
  - The spreadsheet adapter follows the typed `Klyroo.Spreadsheet`: `RegisterSpreadsheet` takes the non-generic `SpreadsheetImporter` base, so a `SpreadsheetImporter<TRow>` is registered as is. A column is addressed by its key, which is the property name (`Name`) unless the schema calls `Key("name")`; the `cells` of a row record, the `field` of an issue and the `key` of a column use that key.
  - The multiple choice options of `Query` report `Selected` for every chosen value; they always said `false`, because the text of the list was compared with the option.
- **Breaking (preview):** the text of an exception that an action throws is no longer part of `AgentActResult.Error`, since it can hold a path, a query or a connection string. The step fails with `failed: the application reported an error` and the exception goes to the new `AgentBridgeService.Logger` (`ILogger?`). A spreadsheet `submit` that ends in an error reports its status (`the submit ended with status Error`), and `AgentSpreadsheetSummary.SubmitError` is `null` after an ordinary exception; a `SpreadsheetUserMessageException` thrown by the callback keeps its message, as in `Klyroo.Spreadsheet`. `Klyroo.Agent` now references `Microsoft.Extensions.Logging.Abstractions`.
- **Breaking (preview):** `RegisterForm` defaults `confirmSubmit` to `true`, so a form is not submitted by an agent until an `AllowPolicy` or `allow` callback approves it. Pass `confirmSubmit: false` to keep the old behavior.
- `ActAsync` calls the per-call `allow` callback before `AllowPolicy` (it used to be after), so a step that the callback refuses, such as a confirmation step in `act`, never prompts the person `AllowPolicy` asks. A call is limited to `AgentBridgeService.MaxStepsPerAct` (50) steps, and the `act` schema says so.
- Form fields that are not in effect (condition false, hidden container, tab or section, skipped step) are absent from `Snapshot` and `Query` and refuse every step, by the rule `FormController.ActiveFields` uses for the output; before, only fields with a false `Condition` were flagged off-page and `Query` still listed their choices. `OffPage` now means a field of another step that is in effect. `Hidden` fields are never registered.
- `clear` stores the empty value of the field kind (`false` for a checkbox or switch, an empty list for tags and multiple choices) and checkboxes accept it.
- JSON numbers keep the digits as sent (`AgentArgument` holds the JSON value), never a silently dropped `null`; text fields and spreadsheet cells keep the exact digits. A whole number written `25.0` is accepted for `limit`, `offset`, row numbers and header indexes. The `act` schema lists the argument kinds the parser accepts (no objects).
- `setView` fails when the key is not an enabled view.
- `klyroo-agent.js`: a `data-agent-confirm` step needs a policy that returns exactly `true`; `act` refuses readonly and `aria-readonly` elements, runs only the actions the snapshot lists, prefers an exact ref over a label, treats `visibility: hidden`, `inert` and `aria-hidden` ancestors as not displayed, accepts at most 50 steps, and `createAgent({ root })` scopes snapshot, query and act to one subtree. Refs are weak and pruned when an element is removed. The select no longer advertises `open` and `close`.
- **Breaking (preview):** steps that need confirmation are refused when neither `AllowPolicy` nor the `allow` argument of `ActAsync` is set. Before, `ConfirmRequired` was only a flag and such steps ran. To allow them, set a policy such as `agent.AllowPolicy = (step, confirm) => Task.FromResult(!confirm || userApproved)`, or `(_, _) => Task.FromResult(true)` to approve everything. The same default applies to `data-agent-confirm` elements in `klyroo-agent.js`: pass `allow` to `createAgent`.
- **Breaking (preview):** a spreadsheet `editCell` takes the row number the user sees in the file (the header is usually row 1, so the first data row is 2) instead of an internal 0-based index, and `ObservedChange.Row` reports that number. The spreadsheet snapshot value is an `AgentSpreadsheetSummary` instead of the step key; read `Step` for the old value.
- **Breaking (preview):** the `AgentToolDefinition` record has a sixth parameter, `InputSchema`.
- Every component, the dashboard filters included, reads its step arguments as JSON values (`IReadOnlyList<JsonElement>` inside the bridge), so a host that passes raw JSON and a .NET caller that passes plain values give the same result.

### Removed

- `klyroo-behaviors.js` and the `@klyroo/behaviors` source. The module imported a `./shared.js` that was never shipped, so it could not load. The same helpers are in the Klyroo.UI script as `Klyroo.behaviors` and `Klyroo.definePrimitive`.

### Fixed

- A form `setValue` stored the raw argument. A model's `JsonElement` or text now becomes the value the field kind stores (a number, a boolean, a date text, a list of option values) with the invariant culture, and a select, radio or autocomplete value that is not an enabled option is refused with the reason. See the table in [Concepts](docs/concepts.md#values-the-model-sends).
- `klyroo-agent.js` `act` ran actions on disabled and hidden elements and let an exception from an action or a policy reject the whole call. It now refuses them and returns `{ ok: false, error }`.
- A step whose action throws, such as a failing `OnSubmit` or a non-numeric `editCell` row, now fails the run with an `Error` instead of throwing out of `ActAsync`.
- A dashboard filter `setValue` toggled the value, so setting a selected value removed it, and `clear` restored the defaults. `setValue` now sets the selection, `clear` empties it (multiple filters only), and an unknown value or a selection over `Max` fails the step instead of reporting success.
- Steps on a component that is not displayed, such as a hidden conditional field, are refused.
- An exception thrown by an `Observe` callback escaped `ActAsync` after the step was applied, ending the run and skipping the other subscribers. It is now ignored.

## [0.1.0-preview.1] - 2026-11-24

### Added

- `AgentBridgeService` with `Snapshot`, `Query` and `ActAsync`: list registered components with stable references, list the choices of a select or filter without opening a popup, and run named actions by reference or label, stopping at the first failing step.
- Registration of Klyroo.Form forms (including stepped forms), Klyroo.Dashboard dashboards and Klyroo.Spreadsheet importers with `RegisterForm`, `RegisterDashboard` and `RegisterSpreadsheet`.
- Permission gating with `AllowPolicy`, a per-call `allow` callback and confirmation-required forms.
- Redaction of password and one-time-code values in snapshots, results and observed changes.
- `Observe` change stream delivering an `ObservedChange` for each successful step, and `GetToolDefinitions` describing the `snapshot`, `query` and `act` tools with read-only and consequential hints.
- `AddKlyrooAgent()` dependency injection registration (scoped).
- `UndoStack<T>`, a bounded undo and redo history, in the dependency-free, NativeAOT-compatible `Klyroo.Agent.Core` package.
- Browser runtime `klyroo-agent.js` (snapshot, query, act, observe and WebMCP registration) shipped as a static web asset of `Klyroo.Agent`.

[Unreleased]: https://github.com/klyroo/klyroo-agent/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/klyroo/klyroo-agent/releases/tag/v0.1.0-preview.1
