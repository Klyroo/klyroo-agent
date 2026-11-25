# Klyroo.Agent

*Part of [Klyroo](https://klyroo.dev): Build better. Compose freely.*

[![CI](https://github.com/klyroo/klyroo-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/klyroo/klyroo-agent/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/badge/NuGet-0.1.0--preview.1-004880.svg)](https://www.nuget.org/packages/Klyroo.Agent) [![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/) [![NativeAOT](https://img.shields.io/badge/NativeAOT-Compatible-10b981.svg)](https://klyroo.dev/why) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)

> **Semantic AI Agent Bridge (snapshot/query/act), WebMCP tools, observe stream & UndoStack**

Inspired by ui-sdk.dev: makes Klyroo.Form, Klyroo.Dashboard, and Klyroo.Spreadsheet immediately inspectable and operable by AI agents without brittle CSS selectors. Provides semantic Snapshot/Query/Act, global permission policies, automatic [redacted] secret masking, WebMCP browser assets, and UndoStack<T>.

[![MAUI Hybrid](https://img.shields.io/badge/.NET%20MAUI-Blazor%20Hybrid-8b5cf6.svg)](./docs/maui-and-aot.md)

---

## Architecture (Two-Layer Headless Split)

- **`Klyroo.Agent.Core`** — 100% headless C#, zero UI dependencies, `<IsAotCompatible>true</IsAotCompatible>`, `<IsTrimmable>true</IsTrimmable>`. Runs in Minimal APIs, background workers, CLI apps, and native C# .NET MAUI ViewModels.
- **`Klyroo.Agent`** — Multiplatform Blazor Web (Static SSR, Interactive Server, WebAssembly, Auto) & `.NET MAUI Blazor Hybrid` (`browser`, `ios`, `android`, `maccatalyst`, `windows`).

## Highlights

- **Semantic Snapshot, Query & Act** (`Semantic Bridge`) — Exposes up to 100 controls by stable ref (c1, c2…), queries select/filter choices without opening popups, and runs semantic actions (setValue, toggle, submit).
- **Permission Gate & Confirmation Policy** (`Policy Guard`) — Global AllowPolicy or per-call gate with ConfirmRequired flags to approve or block consequential actions before execution.
- **Observe Stream & Secret Redaction** (`Privacy First`) — Unified ObservedChange stream (Agent/User source) where Password and OneTimeCode fields are always redacted as [redacted].
- **WebMCP JS Assets & UndoStack<T>** (`WebMCP + Undo`) — Ships _content/Klyroo.Agent/klyroo-agent.js + klyroo-behaviors.js for WebMCP and a bounded generic UndoStack<T> in Klyroo.Agent.Core (NativeAOT).

## Prerequisites

- .NET 10 SDK or later.
- A Blazor Web App (Static SSR, Interactive Server, WebAssembly or Auto) or a .NET MAUI Blazor Hybrid app for the UI
  package; the `.Core` package runs anywhere .NET 10 runs, NativeAOT included.
- For .NET MAUI: the `maui` workload (`dotnet workload install maui`).

## Installation

```bash
dotnet add package Klyroo.Agent.Core   # Headless C# / NativeAOT / MAUI C#
dotnet add package Klyroo.Agent        # Blazor Web & .NET MAUI Blazor Hybrid
```

Register services in `Program.cs` (Blazor Web App) or `MauiProgram.cs` (.NET MAUI Blazor Hybrid):

```csharp
builder.Services.AddKlyrooAgent();
```

## Quick Start

### Headless Core (`Klyroo.Agent.Core`)

```csharp
using Klyroo.Agent;
using Klyroo.Form;
using Klyroo.Dashboard;
using Klyroo.Spreadsheet;

agent.RegisterForm(form, confirmSubmit: true);
agent.RegisterDashboard(dashboard);

// Inspect choices accent-insensitively without opening any popup
var choices = agent.Query(new AgentQueryRequest { Within = "region", Text = "Europe" });

// Execute semantic steps with policy checks & automatic secret redaction
var result = await agent.ActAsync([
    new AgentActStep("project name", "setValue", ["Atlas"]),
    new AgentActStep("region", "setValue", ["eu-west"])
]);
```

### Blazor Web & .NET MAUI Hybrid (`Klyroo.Agent`)

```razor
// Served automatically via _content/Klyroo.Agent/klyroo-agent.js
const snap = window.Klyroo.agent.snapshot();
const res  = await window.Klyroo.agent.act([
  { ref: 'Project name', action: 'setValue', args: ['Atlas'] }
]);

// Privacy-preserving observation stream (passwords auto-masked as "[redacted]")
const stop = window.Klyroo.agent.observe(evt => console.log(evt));
```

## Documentation

| Guide | Description |
| :--- | :--- |
| [Online documentation](https://docs.klyroo.dev/agent) | Guides, live examples and API reference |
| [Getting Started](./docs/getting-started.md) | Installation, DI registration, stylesheets, and first working example |
| [Architecture](./docs/architecture.md) | Headless `.Core` vs UI split, NativeAOT guarantees, and platform matrix |
| [API Reference](./docs/api-reference.md) | Exhaustive reference of every public class, enum, property, method, and Razor parameter |
| [MAUI & NativeAOT](./docs/maui-and-aot.md) | `.NET MAUI Blazor Hybrid` (`iOS`, `Android`, `macOS`, `Windows`) and NativeAOT deployment |
| [Recipes & Testing](./docs/recipes.md) | End-to-end production patterns and `xUnit` / `bUnit` testing recipes |
| [AI Agent Skill](./skills/klyroo-agent/SKILL.md) | Machine-readable guidance for coding agents |

## Key Public Types

| Type | Kind | Package | Summary |
| :--- | :--- | :--- | :--- |
| `AgentChangeSource` | `enum` | `Klyroo.Agent.Core` | Source of a component state change reported to `AgentBridgeService.Observe`. |
| `ObservedChange` | `record` | `Klyroo.Agent.Core` | A single notification emitted by the unified `observe` change stream. |
| `AgentComponentSnapshot` | `record` | `Klyroo.Agent.Core` | Snapshot entry describing one component on the page and the actions it currently supports. |
| `AgentChoiceItem` | `record` | `Klyroo.Agent.Core` | One choice item returned by `AgentBridgeService.Query` for a select, filter, or list component. |
| `AgentQueryMatch` | `record` | `Klyroo.Agent.Core` | Result entry returned by `AgentBridgeService.Query`. |
| `AgentQueryRequest` | `class` | `Klyroo.Agent.Core` | Query parameters for `AgentBridgeService.Query`. |
| `AgentActStep` | `record` | `Klyroo.Agent.Core` | One action step in an `AgentBridgeService.ActAsync` call. |
| `AgentActStepOutcome` | `record` | `Klyroo.Agent.Core` | Outcome of a single step in `AgentActResult`. |
| `AgentActResult` | `record` | `Klyroo.Agent.Core` | Result of an `AgentBridgeService.ActAsync` invocation. |
| `AgentToolDefinition` | `record` | `Klyroo.Agent.Core` | Tool metadata and hints following the `ui-sdk.dev` Agent / WebMCP specification. |
| `UndoStack<T>` | `class` | `Klyroo.Agent.Core` | Generic undo/redo snapshot stack inspired by `createUndoStack` from `ui-sdk.dev`. |
| `AgentBridgeService` | `class` | `Klyroo.Agent` | Semantic Agent Bridge inspired by `ui-sdk.dev` (`createAgent`, `snapshot`, `query`, `act`, `observe`). Allows AI agents and in-app assistants to read component state, query option lists without opening popups, execute semantic actions with permission checks, and subscribe to a unified `ObservedChange` stream with automatic redaction of sensitive fields. |
| `KlyrooAgentServiceCollectionExtensions` | `class` | `Klyroo.Agent` | Public API type |

## Building & Testing

```bash
dotnet build
dotnet test
dotnet pack -c Release
```

## Contributing

Issues and pull requests are welcome. Read [CONTRIBUTING.md](./CONTRIBUTING.md) for the workflow (Conventional
Commits, tests, documentation), follow the [Code of Conduct](./CODE_OF_CONDUCT.md), and report vulnerabilities
privately as described in [SECURITY.md](./SECURITY.md).

## Migrating from Lux

This library was previously named `lux-agent`. [MIGRATION.md](./MIGRATION.md) lists every renamed package, namespace,
component, CSS token and build setting.

## License

MIT — see [LICENSE](./LICENSE). Part of the **[Klyroo](../klyroo-stack/README.md)** ecosystem.
