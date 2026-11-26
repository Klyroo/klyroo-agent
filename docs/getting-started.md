# Getting started

By the end of this page an agent-style caller will read and edit a form through `AgentBridgeService`, first in a
console app and then inside a Blazor component.

Check the [prerequisites](../README.md#prerequisites) first: the .NET 10 SDK, and for the components a Blazor app with Klyroo.UI set up.

## 1. Install

```bash
dotnet add package Klyroo.Agent --prerelease
```

`Klyroo.Agent` brings `Klyroo.Agent.Core` and the Core packages of Klyroo.Form, Klyroo.Dashboard and
Klyroo.Spreadsheet with it. If you only need the data types and `UndoStack<T>`, install `Klyroo.Agent.Core`.
There is no stylesheet to reference.

## 2. Register the bridge

In `Program.cs` of a Blazor app, or in `MauiProgram.cs` after `AddMauiBlazorWebView()`:

```csharp
using Klyroo.Agent;

builder.Services.AddKlyrooAgent();
```

The service is scoped: each Blazor circuit, request or MAUI scope gets its own `AgentBridgeService`. In an
interactive Auto app, register it on both the server and the client project.

## 3. Add imports

In `_Imports.razor`:

```razor
@using Klyroo.Agent
@using Klyroo.Form
```

Add `Klyroo.Dashboard` and `Klyroo.Spreadsheet` the same way if you register those controllers.

## 4. Build a working example, headless

The bridge works on controllers, so you can try it without a UI. This console program registers a form and runs
the three operations.

```csharp
using Klyroo.Agent;
using Klyroo.Form;

var agent = new AgentBridgeService();

var schema = FormSchema.For<Project>(s =>
{
    s.FormKey = "project-settings";
    s.Title = "Project settings";
    s.Field(p => p.Name, f => f.Label("Project name"));
    s.Field(p => p.Region, f => f.Options(
    [
        new FormOption<string>("us-east", "US East"),
        new FormOption<string>("eu-west", "Europe West"),
    ]));
});
var form = new FormController<Project>(schema, new Project());

agent.RegisterForm(form);
```

`Snapshot` shows what an agent sees. Each entry has a reference, a component kind, the label and the current value:

```csharp
foreach (var component in agent.Snapshot())
{
    Console.WriteLine($"{component.Ref} {component.Component} \"{component.Label}\" = {component.Value?.ToDisplayString()}");
}
// c1 input "Project name" = Apollo
// c2 select "Region" = us-east
// c3 form "Project settings" = clean
```

`Query` lists the choices of the select without opening anything:

```csharp
var matches = agent.Query(new AgentQueryRequest { Within = "region", Text = "europe" });
Console.WriteLine(matches[0].Items[0].Value); // eu-west
```

`ActAsync` runs steps in order. The first argument of a step is a component reference (`c1`) or its label, in any case:

```csharp
var result = await agent.ActAsync(
[
    new AgentActStep("Project name", "setValue", ["Atlas"]),
    new AgentActStep("Region", "setValue", ["eu-west"]),
]);

Console.WriteLine(result.Ok ? "done" : result.Error);
Console.WriteLine(form.Get(schema.Field(p => p.Name))); // Atlas

public sealed class Project
{
    public string Name { get; set; } = "Apollo";

    public string Region { get; set; } = "us-east";
}
```

The argument of a step is an `AgentArgument`, a small value over the JSON a model would send. Text, `bool`, `int`, `long`,
`decimal`, `double` and `string[]` convert to it, so `["Atlas"]`, `[12]` and `[new[] { "us", "eu" }]` are enough, and a
number keeps its digits.

If a step fails, `result.Ok` is `false`, `result.Error` names the step and the reason, and the later steps do not
run. Earlier steps stay applied.

## 5. Use it from Blazor

Inject the scoped service in the component that owns the controller and register the controller once, in
`OnInitialized`:

```razor
@using Klyroo.Agent
@using Klyroo.Form
@inject AgentBridgeService Agent

<h1>@_form.Title</h1>
<p>Project name: @_form.Get(_name)</p>

@code {
    private static readonly FormSchema<Project> Schema = FormSchema.For<Project>(s =>
    {
        s.FormKey = "project-settings";
        s.Title = "Project settings";
        s.Field(p => p.Name, f => f.Label("Project name"));
    });

    private readonly FormField<Project, string> _name = Schema.Field(p => p.Name);
    private FormController<Project> _form = default!;

    protected override void OnInitialized()
    {
        _form = new FormController<Project>(Schema, new Project());
        _form.Changed += () => InvokeAsync(StateHasChanged);

        Agent.RegisterForm(_form, confirmSubmit: true);
    }
}
```

The component renders the same controller that the agent edits, so a change made through `ActAsync` shows up in
the page after `Changed` fires. Render the form with the Klyroo.Form components if you use them; the bridge does
not care how the controller is displayed.

Something still has to call `Snapshot`, `Query` and `ActAsync`: your own endpoint, a SignalR hub, an in-app
assistant, or an MCP server. [Expose the three operations as tools](guides.md#expose-the-operations-as-llm-tools)
shows a handler you can start from, and [the chat guide](guides.md#add-an-in-page-chat-in-the-same-blazor-circuit)
wires an `IChatClient` to the bridge. Because this form is registered with `confirmSubmit: true`, a `submit` step is
refused until you set an `AllowPolicy`, as the [approval guide](guides.md#require-approval-before-consequential-actions) shows.

## Next steps

- [Require approval before consequential actions](guides.md#require-approval-before-consequential-actions)
- [Guides](guides.md)
- [Concepts](concepts.md)
- [Platforms](platforms.md)
- [API reference](api-reference.md)
