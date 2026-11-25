# Klyroo.Agent

A semantic bridge that lets AI agents read and operate Klyroo forms, dashboards and spreadsheet imports from .NET code.

[![CI](https://github.com/klyroo/klyroo-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/klyroo/klyroo-agent/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Klyroo.Agent.svg)](https://www.nuget.org/packages/Klyroo.Agent)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

An agent that drives a UI through CSS selectors breaks whenever the markup changes. Klyroo.Agent skips the markup:
you register the controllers behind your UI, and the agent works with their labels, values and named actions. The
bridge is plain C# with no JavaScript interop, so it behaves the same in every Blazor render mode, in .NET MAUI and
in a console app.

## Features

- **Snapshot**: lists every registered component with a stable reference (`c1`, `c2`, ...), its label, current value and the actions it accepts, capped at 100 entries.
- **Query**: lists the choices of a select or filter, filtered by text, without opening a popup. Matching ignores case and accents.
- **Act**: runs named actions (`setValue`, `toggle`, `clear`, `reset`, `submit`, `nextStep`, `setView`, `refresh`, `editCell`) by reference or label, stops at the first failing step and reports why.
- **Permission gate**: `AllowPolicy` and a per-call `allow` callback can refuse any step, and forms can be flagged as confirmation-required.
- **Secret redaction**: password and one-time-code fields appear as `[redacted]` in snapshots, results and observed changes.
- **Observe**: `Observe` delivers one `ObservedChange` per successful step, with the previous and new value.
- **Tool definitions**: `GetToolDefinitions` returns the `snapshot`, `query` and `act` tool descriptions, with read-only and consequential hints, ready to hand to an LLM or MCP host.
- **Browser runtime**: a separate JavaScript module (`klyroo-agent.js`) offers the same three operations over the DOM and can register them with WebMCP when the browser provides it.

## Packages

| Package | Description |
| :--- | :--- |
| [`Klyroo.Agent.Core`](https://www.nuget.org/packages/Klyroo.Agent.Core) | The data types of the bridge (`AgentActStep`, `AgentActResult`, `ObservedChange`, ...) and `UndoStack<T>`. No dependencies, NativeAOT compatible. |
| [`Klyroo.Agent`](https://www.nuget.org/packages/Klyroo.Agent) | `AgentBridgeService`, `AddKlyrooAgent()`, and the browser runtime served from `_content/Klyroo.Agent/`. Depends on `Klyroo.Agent.Core`, `Klyroo.Form.Core`, `Klyroo.Dashboard.Core` and `Klyroo.Spreadsheet.Core`. |
| [`@klyroo/agent`](packages/agent/README.md) | Source of the browser runtime (npm package manifest). Not published to npm; the same file ships inside `Klyroo.Agent`. |
| [`@klyroo/behaviors`](packages/behaviors/README.md) | Source of a set of headless UI behaviors (focus, selection, popup, undo). Not published to npm; the same file ships inside `Klyroo.Agent`. |

## Installation

```bash
dotnet add package Klyroo.Agent --prerelease
```

Register the bridge in `Program.cs` (Blazor) or `MauiProgram.cs` (.NET MAUI Blazor Hybrid):

```csharp
builder.Services.AddKlyrooAgent();
```

`AgentBridgeService` is registered as a scoped service. The package has no stylesheet. If you only need the data
types, install `Klyroo.Agent.Core` instead.

## Quick start

Register a form, then let a caller inspect it and change it:

```csharp
using Klyroo.Agent;
using Klyroo.Form;

var agent = new AgentBridgeService();

var form = new FormController(new FormSchema
{
    FormKey = "project-settings",
    Title = "Project settings",
    Fields =
    [
        new FormFieldSchema { Key = "name", Label = "Project name", DefaultValue = "Apollo" },
        new FormFieldSchema
        {
            Key = "region",
            Label = "Region",
            Kind = FormFieldKind.Select,
            DefaultValue = "us-east",
            Options =
            [
                new FormOptionItem("us-east", "US East"),
                new FormOptionItem("eu-west", "Europe West"),
            ],
        },
    ],
});

agent.RegisterForm(form);

foreach (var component in agent.Snapshot())
{
    Console.WriteLine($"{component.Ref} {component.Component} \"{component.Label}\" = {component.Value}");
}
// c1 input "Project name" = Apollo
// c2 select "Region" = us-east
// c3 form "Project settings" = clean

var matches = agent.Query(new AgentQueryRequest { Within = "region", Text = "europe" });
Console.WriteLine(matches[0].Items[0].Value); // eu-west

var result = await agent.ActAsync(
[
    new AgentActStep("Project name", "setValue", ["Atlas"]),
    new AgentActStep("Region", "setValue", ["eu-west"]),
]);
Console.WriteLine(result.Ok ? "done" : result.Error);
```

In a Blazor component, inject the scoped service instead of creating one, as shown in
[Getting started](docs/getting-started.md).

## Documentation

| | |
| :--- | :--- |
| [Getting started](docs/getting-started.md) | Step-by-step first integration |
| [Guides](docs/guides.md) | Task-oriented recipes |
| [Concepts](docs/concepts.md) | How the library works |
| [Platforms](docs/platforms.md) | Render modes, .NET MAUI, NativeAOT |
| [API reference](docs/api-reference.md) | Every public type and member |
| [FAQ](docs/faq.md) | Troubleshooting and limitations |

## Requirements

- .NET 10 SDK.
- `Klyroo.Agent.Core` declares `IsAotCompatible` and `IsTrimmable`, so it works in NativeAOT and trimmed apps.
- `Klyroo.Agent` declares `IsTrimmable` and the supported platforms `browser`, `ios`, `android`, `maccatalyst` and `windows`. It does not declare `IsAotCompatible`.
- `AgentBridgeService` has no dependency on a render mode: it works in Static SSR, Interactive Server, WebAssembly, Auto, .NET MAUI Blazor Hybrid and non-UI hosts.
- The browser runtime needs a browser; WebMCP registration is skipped when the browser has no `modelContext` API.

## Klyroo ecosystem

Klyroo is a family of independent .NET libraries that compose. Each one has its own repository, versions and releases.

| Library | Description |
| :--- | :--- |
| [Klyroo.UI](https://github.com/klyroo/klyroo-ui) | Accessible Blazor components for the web and .NET MAUI Blazor Hybrid, styled with Tailwind CSS v4 |
| [Klyroo.Query](https://github.com/klyroo/klyroo-query) | Typed query model, URL state sync, and IQueryable / EF Core execution |
| [Klyroo.Table](https://github.com/klyroo/klyroo-table) | Data lists with table and card-grid layouts, filters, selection and actions |
| [Klyroo.Form](https://github.com/klyroo/klyroo-form) | Schema-driven forms, validation and wizards |
| [Klyroo.Dashboard](https://github.com/klyroo/klyroo-dashboard) | Dashboard controller, filters and Blazor blocks: stats, gauges, charts, tables and feeds |
| [Klyroo.Display](https://github.com/klyroo/klyroo-display) | Read-only display blocks, media player controls and file preview |
| [Klyroo.Spreadsheet](https://github.com/klyroo/klyroo-spreadsheet) | Import wizard for CSV, TSV and pasted data, with column and value mapping |
| **[Klyroo.Agent](https://github.com/klyroo/klyroo-agent)** | Bridge that lets AI agents read and act on Klyroo forms, dashboards and imports |
| [Klyroo.Stack](https://github.com/klyroo/klyroo-stack) | Meta-package, MCP documentation server and `dotnet new` templates |

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request, and follow the
[code of conduct](CODE_OF_CONDUCT.md). Report security issues privately as described in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
