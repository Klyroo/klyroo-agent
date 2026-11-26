# Concepts

## The model

An agent needs three things from a UI: to know what is there, to know what it can choose, and a way to change it.
`AgentBridgeService` offers exactly those as `Snapshot`, `Query` and `ActAsync`, over a list of registered
components. A component is not a rendered element. It is a small record the bridge builds from a controller:

```text
 Klyroo.Form      FormController        --RegisterForm-------+
 Klyroo.Dashboard DashboardController   --RegisterDashboard--+--> AgentBridgeService
 Klyroo.Spreadsheet SpreadsheetImporter --RegisterSpreadsheet-+        |
                                                                       +-- Snapshot() -> AgentComponentSnapshot[]
 your tool handler / assistant / MCP server --------------------------+-- Query()    -> AgentQueryMatch[]
                                                                       +-- ActAsync() -> AgentActResult
                                                                       +-- Observe()  -> ObservedChange
```

Each registered component has a reference, a kind, a label, a value getter, a list of action names, an optional
disabled and off-page check, and a sensitive flag. Registration is where the library decides which kind a control
gets and which actions it offers:

| Registered object | Components created | Actions |
| :--- | :--- | :--- |
| Form field | `select` (Select, AutoComplete, Radio), `checkbox` (Checkbox, Switch), `number` (Number, Slider), otherwise `input` | `setValue`, `clear`, `reset`; checkboxes offer `toggle`, `setValue`, `clear` (stores `false`), `reset` |
| Hidden form fields | none | never registered: the agent can neither read nor write them |
| The form | `form` | `submit`, `reset`; stepped forms also `nextStep`, `previousStep` |
| Dashboard filter | `select` | `setValue`, `reset`; multiple filters also `toggle`, `clear` |
| The dashboard | `dashboard` | `setView`, `resetFilters`, `refresh` |
| Spreadsheet importer | `spreadsheet` | `assignColumn`, `ignoreColumn`, `resetColumn`, `answerValue`, `editCell`, `discardRows`, `restoreRows`, `nextStep`, `previousStep`, `submit`, `clearFile` (the last two need confirmation) |

Stateless form fields (the kinds `Info`, `Divider`, `Section` and `Button`) are skipped. References are assigned in
registration order as `c1`, `c2`, and so on, and never change for the life of the service.

## How a step is resolved

`ActAsync` takes a list of steps and runs them one at a time. For every step it does the following, and stops at the
first check that fails:

1. **Find the target.** The step's `Ref` is matched against component references first, then against labels
   ignoring case, accents and repeated whitespace. No match is an error. A label shared by several components is also an
   error, which lists the references so the caller can retry with one.
2. **Check state.** A component whose disabled check returns true rejects the step.
3. **Check the action.** The action name must be in the component's action list.
4. **Ask the policy.** A step that needs confirmation is refused when neither `AllowPolicy` nor an `allow` callback is
   set. Otherwise the `allow` callback passed to the call runs first, then `AllowPolicy`, and either returning `false`
   refuses the step. The per-call gate goes first so that a step it refuses never prompts the person `AllowPolicy` asks.
5. **Run it.** The action calls the controller. An action that returns `false`, or refuses its argument with a reason
   (a select value that is not an option, a row number the file does not have), fails the step and the message says why.
6. **Report it.** The new value is stored in `AgentActResult.Values`, the step is added to `Steps`, and one
   `ObservedChange` is delivered to subscribers.

A form field that is not in effect refuses every step and is absent from `Snapshot` and `Query`. "In effect" is the rule
`FormController.ActiveFields` applies to the form output: the field's condition is true, its containers, tab and section
are visible, and its step is the current one or is not skipped. A field of another step that is in effect is listed only by
`Snapshot(includeOffPage: true)`, flagged `OffPage`, and `Query` leaves it out. A call carries at most
`AgentBridgeService.MaxStepsPerAct` (50) steps; a longer list fails before any step runs.

Failures are values, not exceptions: you get `Ok = false` and an `Error` message that names the step number, the
component and the reason, including when the action throws (a failing `OnSubmit`, for example). That makes the message safe to return to a language model as a tool result so it can
correct itself. There is no rollback. If step 3 of 5 fails, steps 1 and 2 stay applied and 4 and 5 never run.

## Confirmation and policy

The bridge does not decide what is risky in your app. You mark it: `RegisterForm(form, confirmSubmit: true)`, which is the default, lists
`submit` and `reset` in the `ConfirmActions` of the form component (pass `confirmSubmit: false` to let an agent submit freely), and those two actions carry the flag to the policy; moving between steps never does. Some
actions are marked by the bridge itself: a spreadsheet `submit` and `clearFile` always need confirmation, listed in
`ConfirmActions` of the snapshot entry. `AllowPolicy` is the single place where you turn the flag into a decision,
typically by asking the user.

With no `AllowPolicy` and no `allow` callback, a step that needs confirmation is refused with `needs confirmation and
no AllowPolicy is set`, and every other step runs. This is the safe default: an agent cannot submit or clear anything
until you decide how it is approved. The library never opens a dialog, it only asks a function.

## Values the model sends

A model delivers arguments as JSON, so numbers, booleans and lists arrive as `JsonElement` and often as text. `ActAsync`
unwraps them once: a JSON number becomes a `long` when the token is an integer in range, a `decimal` when that reproduces the
token exactly (`1.50` stays `1.50`), and otherwise keeps its digits as sent. Text fields and spreadsheet cells therefore get
the exact digits (`12345678901234567890` stays that text), numeric fields store a `double`, and a whole number written
`25.0` counts as an integer. Then and a form `setValue` then coerces the value to what the field kind stores, with the invariant
culture:

| Field kind | Stored value | Accepted from the model |
| :--- | :--- | :--- |
| Text, Textarea, Password, OneTimeCode, PhoneNumber, Hidden | `string` | text or a number; `MaxLength` is enforced |
| Number, Slider | `double` | a JSON number or numeric text such as `12.5` (no thousands separator); `Min` and `Max` are enforced |
| Rating | `double` | a whole number from `Min` (default 0) to `Max` (default 5) |
| Checkbox, Switch | `bool` | `true`, `false`, `"yes"`, `"no"`, `"on"`, `"off"`, `"1"`, `"0"`, or the number 0 or 1 |
| Date | `yyyy-MM-dd` text | that format, or a full local timestamp read for its date |
| DateTime, Time | `yyyy-MM-ddTHH:mm` and `HH:mm` text | those formats, without a time zone |
| DateRange | list of two dates | `["2026-01-01", "2026-01-31"]`, end not before start |
| ColorPicker | `#rrggbb` text | that format, stored in lower case |
| Select, AutoComplete, Radio | the option value | an option value, or an option label ignoring case and accents; disabled and ambiguous labels are refused |
| CheckboxGroup, multiple Select and AutoComplete | list of option values | a list of values or labels, or one value; duplicates are dropped |
| Tag | list of text | a list of text, trimmed and de-duplicated |
| ArrayList, ArrayTable, containers | none | refused: `setValue` cannot edit a repeatable list |

A value that does not fit fails the step with the reason and leaves the form untouched, so the model can correct
itself. An empty value (`null` or `""`) clears a text, number, date or single choice field, and empties a multiple choice.
The `clear` action stores the empty value of the kind: `false` for a checkbox or switch, an empty list for tags and
multiple choices, `null` elsewhere. `reset` restores the schema default through the form. A dashboard `setView` fails
when the key is not an enabled view.

## Spreadsheet imports

The snapshot value of a spreadsheet is an `AgentSpreadsheetSummary`: the wizard step, the counts of rows, importable,
invalid, discarded and warning rows, the number of open questions, the required columns no file column matches, and the
submit state. It carries no rows, so a large file does not flood the model. To read rows, use `Query` with `Within`
set to the importer and `Collection` set to `rows`, `issues`, `columns`, `headers` or `questions`, with `Offset`, `Limit`
(25 by default, 100 at most), `Filter` and `Text`.

Rows are addressed by the row number the user sees in the file, the `rowNumber` of a row record, for `editCell`,
`discardRows` and `restoreRows`. The header is usually row 1, so the first data row is row 2. Internal indexes never
appear.

## Secrets

Password and one-time-code form fields are marked sensitive at registration. Their value is replaced with
`AgentBridgeService.RedactedValue` in snapshots, in `AgentActResult.Values`, in step outcomes and in both
`Previous` and `Value` of observed changes. The agent can still write to such a field with `setValue`, but it can
never read the value back through the bridge. Redaction is by field kind; a regular text field that happens to hold
a secret is not redacted.

## Two packages

`Klyroo.Agent.Core` holds the vocabulary: the records and enums that describe snapshots, queries, steps, results and
observed changes, plus `UndoStack<T>`. It has no dependencies and is declared NativeAOT compatible, so it fits in a
contract library shared between a server and a client, or in a trimmed app that only needs the types.

`Klyroo.Agent` holds the behavior: `AgentBridgeService` and `AddKlyrooAgent()`. It needs the Core packages of the
Klyroo libraries it can register, so installing it pulls in `Klyroo.Form.Core`, `Klyroo.Dashboard.Core` and
`Klyroo.Spreadsheet.Core`. Despite the Razor SDK project type, it contains no components and no JavaScript
interop; the Blazor-specific part is only the static web asset delivery of the browser runtime.

## Lifetime and threading

`AddKlyrooAgent()` registers the service as scoped, matching the lifetime of the controllers you register. The
service keeps a reference to every controller it is given until you dispose the handle the register method returned,
or until the scope ends. It is not thread safe: calls to `ActAsync`, the register methods and `Observe`
must not overlap. In Blazor Server, call it from code that already runs on the circuit, or wrap the call in `InvokeAsync`.

## The browser runtime

`klyroo-agent.js` is a separate implementation of the same three operations over the DOM. It does not talk to
`AgentBridgeService`, and the two do not share references. Use it when the thing the agent must operate is
markup (for example controls that have no controller) and use the .NET bridge when the logic lives in controllers.

The runtime discovers elements that match `[data-kly-primitive]`, the `kly-*` custom elements of Klyroo.UI,
`[data-slot]` containers for data lists, dashboard filters, form engines and spreadsheet imports, and native
`input`, `select`, `textarea` and `button` elements. It builds the label from `aria-label`, `aria-labelledby`, an
associated `label`, a form-field container, or the placeholder, title or name. Its actions are `setValue`, `clear`,
`toggle`, `setChecked`, `press` and `focus`, plus whatever API a registered primitive exposes. `snapshot` returns at
most 100 elements and `query` at most 50 matches. Elements marked `data-agent-confirm` set `step.confirm` for the
`allow` callback. Like the .NET bridge, it accepts a policy at two levels: `allow` on `createAgent` and `allow` on each `act` call, and it refuses a `data-agent-confirm` element when neither is set. `act` refuses disabled elements (including those inside a disabled `fieldset`) and elements that are not displayed, and turns an exception thrown by an action or a policy into `{ ok: false, error }`. A `data-agent-confirm` step runs only
when a policy returns exactly `true`. `act` refuses edits (`setValue`, `clear`, `toggle`, `setChecked`) on `readonly` and
`aria-readonly` elements, and runs only actions the snapshot lists for the element. An element counts as not displayed
when it, or an ancestor, is `hidden`, `inert` or `aria-hidden="true"`, is in a closed dialog, or CSS hides it
(`display`, `visibility`, `content-visibility`). An exact ref beats a label that looks like one. `createAgent({ root })` limits
snapshot, query and act to one subtree (an element or a function returning one), so the agent cannot act on what it was
not shown; a `within` outside the root is ignored. Refs are weak and dropped when their element leaves the document.
A call accepts at most 50 steps.

`registerWebMcp` publishes `snapshot`, `query` and `act` to the browser's `modelContext` (WebMCP) when
`document.modelContext` or `navigator.modelContext` exposes `registerTool`, and returns a function that
unregisters them. To give a custom widget an agent API, define it with `Klyroo.definePrimitive` from the Klyroo.UI
script, which also exposes the headless helpers (focus, selection, popup, reorder, undo) as `Klyroo.behaviors`. The
runtime finds the result through its `data-kly-primitive` attribute.

## Design decisions

- **Controllers, not markup.** Working on controllers makes the bridge independent of render mode and styling, and
  tests need no browser. The cost is that only things behind a registered controller are visible.
- **Failure as data.** Returning an error message instead of throwing fits tool calling, where the model should
  read the failure and retry. The cost is that callers must check `Ok`.
- **Labels as addresses.** Labels are readable by a model without a prior snapshot, but they can collide. The
  bridge refuses ambiguous labels rather than guessing, and registering the same controller twice therefore makes
  every label ambiguous.
- **Stop at the first failure, no rollback.** Controllers have no common transaction, so the bridge cannot undo a
  step. Keep multi-step runs short, or order steps so a partial run is harmless.
