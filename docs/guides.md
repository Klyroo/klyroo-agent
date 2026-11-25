# Guides

Task-oriented recipes. Each one assumes the bridge is registered as shown in [Getting started](getting-started.md).
Samples use `agent` for an `AgentBridgeService` and `form` for a `FormController` that is already registered.

- [Require approval before consequential actions](#require-approval-before-consequential-actions)
- [Drive a multi-step wizard](#drive-a-multi-step-wizard)
- [Control a dashboard](#control-a-dashboard)
- [Import a spreadsheet through the agent](#import-a-spreadsheet-through-the-agent)
- [Expose the operations as LLM tools](#expose-the-operations-as-llm-tools)
- [Log what the agent changed](#log-what-the-agent-changed)
- [Use the browser runtime and WebMCP](#use-the-browser-runtime-and-webmcp)
- [Keep an undo history](#keep-an-undo-history)
- [Test your integration](#test-your-integration)

## Require approval before consequential actions

Goal: let an agent fill a form but not submit it without a human decision.

Register the form with `confirmSubmit: true` and set an `AllowPolicy`. The policy runs before every step and
receives the step and whether its target was flagged:

```csharp
var approved = false; // set to true when the user clicks "Approve" in your UI

agent.AllowPolicy = (step, confirmRequired) => Task.FromResult(!confirmRequired || approved);
agent.RegisterForm(form, confirmSubmit: true);

var result = await agent.ActAsync([new AgentActStep("Project settings", "submit")]);
// result.Ok is false, result.Error: Step 1: action "submit" on "Project settings" was refused by policy.
```

The flag is only information. If `AllowPolicy` is `null`, nothing is refused, even for flagged targets. The flag
is set on the form component, so it also covers `reset`, `nextStep` and `previousStep`, not only `submit`.

For a decision that belongs to one call, such as a request that carries its own user, pass a callback to
`ActAsync`. It runs after `AllowPolicy`, and both must return `true`:

```csharp
var result = await agent.ActAsync(
    steps,
    allow: (step, confirmRequired) => Task.FromResult(!confirmRequired || userConfirmedThisRequest));
```

## Drive a multi-step wizard

Goal: let an agent fill a stepped form and move between its steps.

A form built from `Steps` exposes `nextStep` and `previousStep` on the form component. Fields of the current and
other steps are all registered; fields whose `Condition` is false are marked `OffPage`.

```csharp
var wizard = new FormController(new FormSchema
{
    FormKey = "onboarding",
    Title = "Onboarding",
    Steps =
    [
        new FormStepSchema
        {
            Key = "account",
            Title = "Account",
            Fields = [new FormFieldSchema { Key = "org", Label = "Organization" }],
        },
        new FormStepSchema
        {
            Key = "confirm",
            Title = "Confirm",
            Fields = [new FormFieldSchema { Key = "ok", Label = "Accepted", Kind = FormFieldKind.Checkbox }],
        },
    ],
});
agent.RegisterForm(wizard);

var result = await agent.ActAsync(
[
    new AgentActStep("Organization", "setValue", ["Klyroo"]),
    new AgentActStep("Onboarding", "nextStep"),
    new AgentActStep("Accepted", "toggle"),
    new AgentActStep("Onboarding", "submit"),
]);
```

`nextStep` and `submit` return `false` when the form refuses them, for example because a required field is empty.
The step then fails with `returned false` and the run stops, so check `result.Error` instead of assuming progress.
Use `agent.Snapshot(includeOffPage: false)` to show an agent only the fields that are rendered.

## Control a dashboard

Goal: let an agent change a dashboard filter and view, and refresh it.

Each filter that is not headless becomes a `select` component. The dashboard itself accepts `setView`,
`resetFilters` and `refresh`:

```csharp
using Klyroo.Dashboard;

var dashboard = new DashboardController("revenue");
dashboard.AddView("overview", "Overview");
dashboard.AddView("cohorts", "Cohorts");
dashboard.AddFilter(new DashboardFilterControl(
    "region",
    "Sales region",
    items: [new DashboardOption("eu", "Europe"), new DashboardOption("us", "United States")],
    defaultValues: ["eu"]));
agent.RegisterDashboard(dashboard);

await agent.ActAsync(
[
    new AgentActStep("Sales region", "setValue", ["us"]),
    new AgentActStep("revenue", "setView", ["cohorts"]),
    new AgentActStep("revenue", "refresh"),
]);
```

On a filter, `setValue` and `toggle` both call the filter's `Toggle(value)`. For a single-select filter that
replaces the selection; for a multiple-select filter it adds or removes the value. The dashboard is addressed by
its key (`revenue`), not by a title.

## Import a spreadsheet through the agent

Goal: let an agent correct cells and submit an import that a user already loaded.

The agent cannot load a file. Load it in your code, register the importer, and the agent can edit cells and move
through the steps:

```csharp
using Klyroo.Spreadsheet;

var importer = new SpreadsheetImporter(new SpreadsheetSchema
{
    Key = "contacts-import",
    Columns = [new SpreadsheetColumnSchema { Key = "name", Label = "Name", Required = true }],
    OnSubmit = rows => Task.FromResult<IReadOnlyList<SpreadsheetRejectedRow>?>([]),
});
importer.LoadText("Name\nAlice\nBob", "contacts.csv");
agent.RegisterSpreadsheet(importer);

var sheet = agent.Snapshot().First(c => c.Component == "spreadsheet");

var result = await agent.ActAsync(
[
    new AgentActStep(sheet.Ref, "editCell", [1, "name", "Robert"]),
    new AgentActStep(sheet.Ref, "nextStep"),
    new AgentActStep(sheet.Ref, "submit"),
]);
```

`editCell` takes the row index, the column key and the new text. The importer's key is the label, so use the
reference when you register more than one. The `submit` step is `Ok` only when the importer ends in the `Done`
status.

## Expose the operations as LLM tools

Goal: connect `Snapshot`, `Query` and `ActAsync` to a tool-calling model or an MCP server.

`GetToolDefinitions` returns the name, description and hints of the three tools. Map each call to the matching
method. This handler reads JSON arguments and works with string values:

```csharp
using System.Text.Json;
using Klyroo.Agent;

foreach (var tool in AgentBridgeService.GetToolDefinitions())
{
    Console.WriteLine($"{tool.Name}: {tool.Description} (consequential: {tool.ConsequentialHint})");
}

static async Task<object> HandleToolCall(AgentBridgeService bridge, string tool, JsonElement args)
{
    switch (tool)
    {
        case "snapshot":
            return bridge.Snapshot(includeOffPage: false);
        case "query":
            return bridge.Query(new AgentQueryRequest
            {
                Within = args.TryGetProperty("within", out var w) ? w.GetString() : null,
                Text = args.TryGetProperty("text", out var t) ? t.GetString() : null,
                Kind = args.TryGetProperty("kind", out var k) ? k.GetString() : null,
            });
        case "act":
            var steps = args.GetProperty("steps").EnumerateArray()
                .Select(s => new AgentActStep(
                    s.GetProperty("ref").GetString()!,
                    s.GetProperty("action").GetString()!,
                    s.TryGetProperty("args", out var a)
                        ? a.EnumerateArray().Select(x => (object?)x.GetString()).ToList()
                        : null))
                .ToList();
            return await bridge.ActAsync(steps);
        default:
            throw new ArgumentException($"Unknown tool '{tool}'.", nameof(tool));
    }
}
```

The definitions carry hints (`ReadOnlyHint`, `ConsequentialHint`, `UntrustedContentHint`) but the library does not
enforce them. Labels and values come from your UI and may contain user text, so treat tool results as untrusted
input to the model, and combine this with an `AllowPolicy` for anything that changes data.

## Log what the agent changed

Goal: keep an audit trail of agent edits.

`Observe` calls your callback after each successful step and returns a handle that unsubscribes when disposed:

```csharp
using var subscription = agent.Observe(change =>
    Console.WriteLine($"{change.Label}: {change.Previous} -> {change.Value} ({change.Reason})"));
```

Password and one-time-code values reach the callback as `[redacted]`. Only changes made through `ActAsync` are
reported, so edits a user types into the page do not appear. The callback runs synchronously inside `ActAsync`, so
keep it fast and do not throw: an exception propagates out of `ActAsync` and ends the run.

## Use the browser runtime and WebMCP

Goal: let an in-browser agent inspect and operate the DOM, including controls that are not backed by a controller.

`Klyroo.Agent` ships `klyroo-agent.js` as a static web asset at `_content/Klyroo.Agent/klyroo-agent.js`. It is an
ES module, so import it from a module script. It is independent of `AgentBridgeService`: it reads the DOM, not
your controllers.

```html
<script type="module">
  import { createAgent } from '/_content/Klyroo.Agent/klyroo-agent.js';

  const agent = createAgent({
    // Return false to refuse a step. step.confirm is true for elements marked data-agent-confirm.
    allow: async (step) => !step.confirm || window.confirm(`Allow ${step.action} on ${step.ref}?`),
  });

  console.log(agent.snapshot());
  await agent.act({ steps: [{ ref: 'Project name', action: 'setValue', args: ['Atlas'] }] });

  // Registers snapshot, query and act with document.modelContext / navigator.modelContext when present.
  const unregister = agent.registerWebMcp();
</script>
```

Mark a field with `data-agent-redact` to hide its value in snapshots and observed changes; password inputs and
`kly-pin-input` are redacted automatically. `registerWebMcp` does nothing when the browser has no `modelContext`
with a `registerTool` function. See [Concepts](concepts.md#the-browser-runtime) for what the module discovers.

## Keep an undo history

Goal: step back and forth through snapshots of your own state.

`UndoStack<T>` is a bounded history in `Klyroo.Agent.Core`. It does not hook into the bridge, so record the
snapshots yourself:

```csharp
using Klyroo.Agent;

var history = new UndoStack<string>("Initial", limit: 50);
history.Record("Updated");
Console.WriteLine(history.Undo()); // Initial
Console.WriteLine(history.Redo()); // Updated
```

Recording a value equal to the current one does nothing, and recording after an undo discards the redo branch.
Use a value or record type for `T`, because equality decides whether a snapshot is a change.

## Test your integration

The bridge is plain C#, so most tests need no browser. This xUnit test checks that an agent can edit a field and
that a flagged submit is refused:

```csharp
using Klyroo.Agent;
using Klyroo.Form;
using Xunit;

public sealed class AgentBridgeTests
{
    [Fact]
    public async Task Agent_can_set_a_value_and_a_denied_submit_changes_nothing()
    {
        var form = new FormController(new FormSchema
        {
            FormKey = "project-settings",
            Title = "Project settings",
            Fields = [new FormFieldSchema { Key = "name", Label = "Project name", DefaultValue = "Apollo" }],
        });
        var agent = new AgentBridgeService
        {
            AllowPolicy = (_, confirmRequired) => Task.FromResult(!confirmRequired),
        };
        agent.RegisterForm(form, confirmSubmit: true);

        var edit = await agent.ActAsync([new AgentActStep("Project name", "setValue", ["Atlas"])]);
        var submit = await agent.ActAsync([new AgentActStep("Project settings", "submit")]);

        Assert.True(edit.Ok);
        Assert.Equal("Atlas", form.GetValue<string>("name"));
        Assert.False(submit.Ok);
        Assert.Contains("refused by policy", submit.Error);
    }
}
```

To check that a component registers its controller, render it with bUnit and call the bridge from the same
service provider. The component is the one from [Getting started](getting-started.md#5-use-it-from-blazor):

```csharp
using Bunit;
using Klyroo.Agent;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class ProjectSettingsTests : BunitContext
{
    [Fact]
    public async Task Component_registers_its_form_with_the_bridge()
    {
        Services.AddKlyrooAgent();
        var cut = Render<ProjectSettings>();
        var agent = Services.GetRequiredService<AgentBridgeService>();

        await cut.InvokeAsync(() => agent.ActAsync([new AgentActStep("Project name", "setValue", ["Atlas"])]));

        cut.WaitForAssertion(() => Assert.Contains("Atlas", cut.Markup));
    }
}
```

Register `AddKlyrooAgent()` before the first `Render`, because the component injects the service while it is
created.
