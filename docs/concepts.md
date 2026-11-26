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
| Form field | `select` (Select, AutoComplete, Radio), `checkbox` (Checkbox, Switch), `number` (Number, Slider), otherwise `input` | `setValue`, `clear`, `reset`; checkboxes offer `toggle`, `setValue`, `reset` instead (no `clear`) |
| The form | `form` | `submit`, `reset`; stepped forms also `nextStep`, `previousStep` |
| Dashboard filter | `select` | `setValue`, `reset`; multiple filters also `toggle`, `clear` |
| The dashboard | `dashboard` | `setView`, `resetFilters`, `refresh` |
| Spreadsheet importer | `spreadsheet` | `editCell`, `nextStep`, `previousStep`, `submit`, `clearFile` |

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
4. **Ask the policy.** `AllowPolicy` runs, then the `allow` callback passed to the call. Either returning `false`
   refuses the step.
5. **Run it.** The action calls the controller. An action that returns `false` fails the step.
6. **Report it.** The new value is stored in `AgentActResult.Values`, the step is added to `Steps`, and one
   `ObservedChange` is delivered to subscribers.

A component that is not displayed (a conditional field whose condition is false) refuses every step.

Failures are values, not exceptions: you get `Ok = false` and an `Error` message that names the step number, the
component and the reason, including when the action throws (a failing `OnSubmit`, for example). That makes the message safe to return to a language model as a tool result so it can
correct itself. There is no rollback. If step 3 of 5 fails, steps 1 and 2 stay applied and 4 and 5 never run.

## Confirmation and policy

The bridge does not decide what is risky. You mark it: `RegisterForm(form, confirmSubmit: true)` sets
`ConfirmRequired` on the form component, and every action on that component carries the flag to the policy.
`AllowPolicy` is the single place where you turn the flag into a decision, typically by asking the user. With no
policy, flagged and unflagged targets behave the same. This keeps the library out of your UI: it never opens a
dialog, it only asks a function.

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
`allow` callback. Like the .NET bridge, it accepts a policy at two levels: `allow` on `createAgent` and `allow` on each `act` call.

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
