# FAQ

## Troubleshooting

**`ActAsync` fails with `ambiguous label ... matched 2 components`**

Two registered components share a label. The usual cause is registering the same controller twice, for example a
component that calls `RegisterForm` on every initialization while the scoped service lives across navigations. The
register methods return a handle: dispose it when the component goes away, so the next
registration does not duplicate the labels, or address components by reference (`c3`) taken from a fresh `Snapshot`.

**A step fails with `component "..." not found`**

The label or reference does not match any registered component. Labels are the field's `Label`, falling back to its
`Key`; the form is labeled by its `Title`, falling back to `FormKey`; a dashboard by its key; an importer by its
schema key. Call `Snapshot()` to see the exact labels. Matching ignores case, accents and repeated spaces, but not
other differences such as punctuation.

**A step fails with `is disabled`**

The controller reports the field as disabled, for example through a `DisabledWhen` rule on a form field, or a
dashboard filter with `Enabled` set to false. Change the state that disables it first.

**A step fails with `action "..." is not supported`**

Each component only accepts the actions listed in its snapshot entry. A plain field has no
`toggle`, and only stepped forms have `nextStep`. Read `Actions` from `Snapshot`.

**A step fails with `failed: the application reported an error`**

The action threw. The text of the exception is not given to the model, because it can hold a path or a connection string. Set
`AgentBridgeService.Logger` to read the exception, for example `agent.Logger = loggerFactory.CreateLogger("agent")`.

**A step fails with `returned false`**

The controller ran the action and declined. Typical causes are a `submit` or `nextStep` on a form that fails
validation. Most refusals carry a reason after `was rejected:`, such as a select value that
is not an option, a row number the file does not have, or a `setView` key that is not an enabled view. Earlier steps stay applied.

**A step fails with `needs confirmation and no AllowPolicy is set`**

The step is a form `submit` or `reset` (they need confirmation by default, `confirmSubmit: false` opts out) or another component registered as confirmation-required, or is a spreadsheet `submit` or `clearFile`, and
nothing decides about it. This is the default on purpose. Set `AllowPolicy` (or pass `allow` to `ActAsync`) to approve
such steps; see [Require approval before consequential actions](guides.md#require-approval-before-consequential-actions).

**A step fails with `refused by policy` but I never set a policy**

`refused by policy` comes from `AllowPolicy`, `refused by caller policy` from the `allow` argument. Check where the
service is configured; `AddKlyrooAgent()` itself sets no policy.

**Submitting a flagged form is refused**

`confirmSubmit: true` (the default) makes a form `submit` and `reset` need confirmation (moving between steps does not), and without an `AllowPolicy` or an `allow`
callback those steps are refused. Earlier previews let them through, which is a breaking change; set a policy that
approves them to get the old behavior. See [Require approval before consequential actions](guides.md#require-approval-before-consequential-actions).

**`Snapshot` is missing components**

`Snapshot` returns at most 100 components. Stateless form fields (`Info`, `Divider`, `Section`, `Button`) are never
registered, hidden inputs are never registered, fields that are not in effect (condition false, hidden container, tab or section, skipped step) are left out, and fields of another step appear only with `includeOffPage`. Dashboard filters marked headless are skipped.

**An observer does not fire when a user edits a field**

`Observe` reports changes made through `ActAsync` only. Subscribe to the controller's own change event for
user edits.

**`/_content/Klyroo.Agent/klyroo-agent.js` returns 404**

Static web assets are served automatically in development. In a published app, map them with `MapStaticAssets` or
`UseStaticFiles`.

## Known limitations

- `ActAsync` has no rollback: a failing step leaves earlier steps applied.
- The bridge never reads password or one-time-code values, but it does not detect other kinds of secrets. Redaction is
  by field kind.
- Only Klyroo.Form (anything that implements `IFormRuntime`), Klyroo.Dashboard and Klyroo.Spreadsheet controllers can be
  registered. Any other source of state needs its own adapter in your code.
- `AgentChangeSource` has seven values, but the .NET bridge only ever reports `Agent`.
- The service is not thread safe.
- The browser runtime and `AgentBridgeService` are independent: references (`c1`) from one are not valid in the other.
- `@klyroo/agent` is not published to npm.

## Questions

**Does it call an LLM?**

No. The library has no model client. It gives you `Snapshot`, `Query` and `ActAsync` plus tool descriptions, and you
connect them to whatever model or MCP server you use.

**Do I need JavaScript?**

No. The .NET bridge runs entirely in C#. The browser runtime is optional and separate.

**Why is `AgentBridgeService` in `Klyroo.Agent` and not in `Klyroo.Agent.Core`?**

It registers Klyroo.Form, Klyroo.Dashboard and Klyroo.Spreadsheet controllers, so it needs their Core packages.
Keeping it out of `Klyroo.Agent.Core` leaves that package free of dependencies.

**Can an agent read a password field?**

No. Password and one-time-code fields report `[redacted]` in every output. The agent can overwrite them with
`setValue`.
