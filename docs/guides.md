# Guides

Task-oriented recipes. Each one assumes the bridge is registered as shown in [Getting started](getting-started.md).
Samples use `agent` for an `AgentBridgeService` and `form` for a `FormController` that is already registered.

- [Require approval before consequential actions](#require-approval-before-consequential-actions)
- [Drive a multi-step wizard](#drive-a-multi-step-wizard)
- [Control a dashboard](#control-a-dashboard)
- [Import a spreadsheet through the agent](#import-a-spreadsheet-through-the-agent)
- [Expose the operations as LLM tools](#expose-the-operations-as-llm-tools)
- [Use the bridge with Microsoft.Extensions.AI](#use-the-bridge-with-microsoftextensionsai)
- [Add an in-page chat in the same Blazor circuit](#add-an-in-page-chat-in-the-same-blazor-circuit)
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

With no `AllowPolicy` and no `allow` argument, a flagged step is refused (`needs confirmation and no AllowPolicy is
set`) and every other step runs, so a missing policy fails closed. Earlier previews ran flagged steps without a policy;
to keep that behavior, set `agent.AllowPolicy = (_, _) => Task.FromResult(true)`, which is only reasonable when a person
drives the agent. The flag is set on the form component, so it also covers `reset`, `nextStep` and `previousStep`, not
only `submit`. A spreadsheet `submit` and `clearFile` are flagged by the bridge itself, whatever you register.

For a decision that belongs to one call, such as a request that carries its own user, pass a callback to
`ActAsync`. It runs after `AllowPolicy`, both must return `true`, and passing it counts as having a policy for flagged steps:

```csharp
var result = await agent.ActAsync(
    steps,
    allow: (step, confirmRequired) => Task.FromResult(!confirmRequired || userConfirmedThisRequest));
```

## Drive a multi-step wizard

Goal: let an agent fill a stepped form and move between its steps.

A form built from `Steps` exposes `nextStep` and `previousStep` on the form component. Fields of the current and
other steps are all registered; fields whose `Condition` is false are marked `OffPage` and refuse every step.

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

On a filter, `setValue` replaces the selection: with one value, or with every value of a list
(`["emea", "amer"]`) on a multiple filter. A multiple filter also accepts `toggle`, which adds or removes one value,
and `clear`, which empties the selection; `reset` restores the defaults. A value that is not an option, or a
selection larger than the filter's `Max`, fails the step without changing anything. The dashboard is addressed by
its key (`revenue`), not by a title.

## Import a spreadsheet through the agent

Goal: let an agent map columns, answer unknown values, fix cells and submit an import that a user already loaded.

The agent cannot load a file. Load it in your code and register the importer:

```csharp
using Klyroo.Spreadsheet;

var importer = new SpreadsheetImporter(new SpreadsheetSchema
{
    Key = "contacts-import",
    Columns =
    [
        new SpreadsheetColumnSchema { Key = "name", Label = "Name", Required = true },
        new SpreadsheetColumnSchema
        {
            Key = "team", Label = "Team", Kind = SpreadsheetColumnKind.Select,
            Options = [new("eng", "Engineering"), new("ops", "Operations")],
        },
    ],
    OnSubmit = rows => Task.FromResult<IReadOnlyList<SpreadsheetRejectedRow>?>([]),
});
importer.LoadText("Full name;Team\nAlice;Engineering\nBob;Devops", "contacts.csv");
agent.RegisterSpreadsheet(importer);
agent.AllowPolicy = async (step, confirm) => !confirm || await AskUserAsync(step); // submit and clearFile need confirmation
```

The snapshot value is a summary, not the rows. Read it first, then page through what you need:

```csharp
var sheet = agent.Snapshot().First(c => c.Component == "spreadsheet");
var summary = (AgentSpreadsheetSummary)sheet.Value!;
// summary.Step, summary.Rows, summary.InvalidRows, summary.OpenQuestions, summary.UnmappedRequiredColumns, summary.CanSubmit

var rows = agent.Query(new AgentQueryRequest { Within = "contacts-import", Collection = "rows", Filter = "invalid", Limit = 25 });
// rows[0].Records: rowNumber, status, cells, issues; rows[0].Total tells whether to ask for the next page with Offset
```

The collections are `rows` (filter `importable`, `invalid`, `discarded`), `issues` (`blocking`, `warnings`), `columns`,
`headers` and `questions` (`open`). Then act. Rows are addressed by the row number the user sees in the file, which is
the `rowNumber` of a record, so with a header on row 1 the first data row is row 2:

```csharp
var result = await agent.ActAsync(
[
    new AgentActStep("contacts-import", "assignColumn", ["Name", "Full name"]),   // field, header text (or the 0-based index as a number)
    new AgentActStep("contacts-import", "answerValue", ["Team", "Devops", "map", "ops"]),
    new AgentActStep("contacts-import", "editCell", [2, "name", "Alice Martin"]), // row number, field, text
    new AgentActStep("contacts-import", "discardRows", [3]),
    new AgentActStep("contacts-import", "nextStep"),
    new AgentActStep("contacts-import", "submit"),                                // needs confirmation
]);
```

`answerValue` takes the field, the raw value from the file, and `map` (with the choice to map to, by value or label),
`leaveEmpty`, `skipRows` or `create`. `ignoreColumn`, `resetColumn` and `restoreRows` undo a mapping decision or a
discard. A step that cannot run says why: an unknown row number, a read-only column, a choice that does not exist,
or the blockers of a `submit` or `nextStep`. The importer's key is the label, so use the reference when you register
more than one. The `submit` step is `Ok` only when the importer ends in the `Done` status with no rejected row.

## Expose the operations as LLM tools

Goal: connect `Snapshot`, `Query` and `ActAsync` to a tool-calling model or an MCP server.

`GetToolDefinitions` returns the name, description, hints and `InputSchema` of the three tools. The schema is a
`JsonElement` with the same shape as the `inputSchema` of the tools in `klyroo-agent.js`, so you can hand it to a model
or an MCP server as is. `InvokeToolAsync` runs a call from its tool name and its raw JSON arguments:

```csharp
using System.Text.Json;
using Klyroo.Agent;

foreach (var tool in AgentBridgeService.GetToolDefinitions())
{
    Console.WriteLine($"{tool.Name}: {tool.Description} (consequential: {tool.ConsequentialHint})");
    Console.WriteLine(tool.InputSchema.GetRawText()); // the JSON Schema of the arguments
}

// toolName and arguments come from the model's tool call.
object result = await agent.InvokeToolAsync(toolName, arguments);
// snapshot: IReadOnlyList<AgentComponentSnapshot>; query: IReadOnlyList<AgentQueryMatch>;
// act: AgentActResult; a malformed call or an unknown tool: AgentToolError (a value, not an exception).
```

Pass the arguments as the `JsonElement` the host gives you. Do not turn each one into text: a number must stay a number
and a list must stay a list, because the form coercion reads them as typed JSON. If you build the steps yourself, keep the
raw elements, and `ActAsync` unwraps them:

```csharp
var steps = args.GetProperty("steps").EnumerateArray()
    .Select(s => new AgentActStep(
        s.GetProperty("ref").GetString()!,
        s.GetProperty("action").GetString()!,
        s.TryGetProperty("args", out var a) ? a.EnumerateArray().Select(x => (object?)x.Clone()).ToList() : null))
    .ToList();
var outcome = await agent.ActAsync(steps);
```

The definitions carry hints (`ReadOnlyHint`, `ConsequentialHint`, `UntrustedContentHint`) but the library does not
enforce them. Labels and values come from your UI and may contain user text, so treat tool results as untrusted
input to the model. What the library does enforce is the policy: steps that need confirmation are refused unless
`AllowPolicy` or the `allow` argument of `InvokeToolAsync` decides, so combine the tools with a policy.

## Use the bridge with Microsoft.Extensions.AI

Goal: give any `IChatClient` the bridge as tools, with a human decision before consequential actions.

Add the `Klyroo.Agent.AI` package, which depends on `Microsoft.Extensions.AI.Abstractions`:

```bash
dotnet add package Klyroo.Agent.AI --prerelease
```

`AsAIFunctions()` returns `snapshot`, `query` and `act` as `AIFunction` instances, with the descriptions and schemas
of the tool definitions, plus `act_with_approval`:

```csharp
using Klyroo.Agent.AI;
using Microsoft.Extensions.AI;

var options = new ChatOptions { Tools = [.. agent.AsAIFunctions()] };
```

`ApprovalRequiredAIFunction` marks a whole function, not one call, so the bridge cannot ask for approval only when a
step needs it. Instead it splits the work in two:

- `act` runs the steps that need no confirmation and stops at the first one that does. Its error names that step and
  tells the model to call `act_with_approval` with the remaining steps, starting there.
- `act_with_approval` is wrapped in `ApprovalRequiredAIFunction`. A `FunctionInvokingChatClient` does not run it: it
  returns a `ToolApprovalRequestContent` that holds the tool call, with its steps, to your code. Show the steps to the
  user and answer with `request.CreateResponse(approved)`. Only an approved call runs, and `AllowPolicy` still applies on
  top of it.

If your own code already decides, for example an `AllowPolicy` that asks the user in your UI, ask for three functions
only: `agent.AsAIFunctions(new AgentAIFunctionOptions { Confirmation = AgentConfirmationMode.UseBridgePolicy })`.
`act` then passes every step to `AllowPolicy`, and refuses flagged steps while it is `null`.

The functions return JSON. Malformed arguments return an object with an `error` field instead of throwing, and a
rejected value (a select option that does not exist, a number out of range) comes back as `ok: false` with the reason,
which the model can read and retry.

## Add an in-page chat in the same Blazor circuit

Goal: a chat box in the page whose assistant sees and edits the very forms the user is looking at.

`AgentBridgeService` is scoped, so every component of a Blazor Server circuit, the chat included, shares one instance
with the controllers registered on the page. Register the services once. The base `IChatClient` comes from your
provider; the bridge does not depend on which one:

```csharp
builder.Services.AddKlyrooAgent();
builder.Services.AddScoped<IChatClient>(sp => /* the IChatClient of your provider */);
```

The component wraps the client with function invocation and keeps the history. When the model calls
`act_with_approval`, the client returns an approval request instead of running it, and the component shows Approve and
Reject buttons:

```razor
@using Klyroo.Agent
@using Klyroo.Agent.AI
@using Microsoft.Extensions.AI
@inject AgentBridgeService Agent
@inject IChatClient BaseClient

<ul>
    @foreach (var message in _history.Where(m => m.Role != ChatRole.Tool && !string.IsNullOrEmpty(m.Text)))
    {
        <li><strong>@message.Role</strong> @message.Text</li>
    }
</ul>

@if (_pending is { ToolCall: FunctionCallContent call })
{
    <p>The assistant wants to run <code>@call.Name</code>: @System.Text.Json.JsonSerializer.Serialize(call.Arguments)</p>
    <button @onclick="() => DecideAsync(true)">Approve</button>
    <button @onclick="() => DecideAsync(false)">Reject</button>
}
else
{
    <input @bind="_draft" @onkeyup="OnKeyUp" disabled="@_busy" />
}

@code {
    private readonly List<ChatMessage> _history = [];
    private IChatClient _chat = default!;
    private ChatOptions _options = default!;
    private ToolApprovalRequestContent? _pending;
    private string _draft = "";
    private bool _busy;

    protected override void OnInitialized()
    {
        // Leave AllowConcurrentInvocation at its default (false): the bridge is not thread safe.
        _chat = new ChatClientBuilder(BaseClient).UseFunctionInvocation().Build();
        _options = new ChatOptions { Tools = [.. Agent.AsAIFunctions()] };
    }

    private async Task OnKeyUp(KeyboardEventArgs e)
    {
        if (e.Key != "Enter" || string.IsNullOrWhiteSpace(_draft)) return;
        _history.Add(new ChatMessage(ChatRole.User, _draft));
        _draft = "";
        await RunAsync();
    }

    private async Task DecideAsync(bool approved)
    {
        _history.Add(new ChatMessage(ChatRole.User, [_pending!.CreateResponse(approved)]));
        _pending = null;
        await RunAsync();
    }

    private async Task RunAsync()
    {
        _busy = true;
        try
        {
            var response = await _chat.GetResponseAsync(_history, _options);
            _history.AddRange(response.Messages);
            _pending = response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().FirstOrDefault();
        }
        finally
        {
            _busy = false;
        }
    }
}
```

Each tool call runs on the server inside the circuit, so a field the assistant sets updates the page through the
`Changed` event of its controller, as long as the page calls `InvokeAsync(StateHasChanged)` from it, as the component in
[Getting started](getting-started.md#5-use-it-from-blazor) does. The user sees the form change while the assistant
works, and a submit waits for the Approve button. Register `Observe` as shown below to keep an audit trail of what the
assistant changed. The model never reads a password field: it is `[redacted]` in every result.

## Log what the agent changed

Goal: keep an audit trail of agent edits.

`Observe` calls your callback after each successful step and returns a handle that unsubscribes when disposed:

```csharp
using var subscription = agent.Observe(change =>
    Console.WriteLine($"{change.Label}: {change.Previous} -> {change.Value} ({change.Reason})"));
```

Password and one-time-code values reach the callback as `[redacted]`. Only changes made through `ActAsync` are
reported, so edits a user types into the page do not appear. The callback runs synchronously inside `ActAsync`, so
keep it fast. An exception it throws is ignored, so it cannot end the run or keep other subscribers from being
called; catch and log inside the callback if you need to know about it.

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

Elements marked `data-agent-confirm` are refused unless `allow` is set; `act` also refuses disabled and hidden elements and
returns `{ ok: false, error }` when an action throws. Mark a field with `data-agent-redact` to hide its value in snapshots and observed changes; password inputs and
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
