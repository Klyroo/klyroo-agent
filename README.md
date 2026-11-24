# Lux.Agent (LuxStack)

> **Semantic AI Agent Bridge (snapshot/query/act), WebMCP tools, observe stream & UndoStack**

Inspired by ui-sdk.dev: makes Lux.Form, Lux.Dashboard, and Lux.Spreadsheet immediately inspectable and operable by AI agents without brittle CSS selectors. Provides semantic Snapshot/Query/Act, global permission policies, automatic [redacted] secret masking, WebMCP browser assets, and UndoStack<T>.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![NativeAOT](https://img.shields.io/badge/Core-NativeAOT%20Ready-10b981.svg)](./docs/maui-and-aot.md)
[![MAUI Hybrid](https://img.shields.io/badge/.NET%20MAUI-Blazor%20Hybrid-8b5cf6.svg)](./docs/maui-and-aot.md)

---

## Architecture (Two-Layer Headless Split)

- **`Lux.Agent.Core`** — 100% headless C#, zero UI dependencies, `<IsAotCompatible>true</IsAotCompatible>`, `<IsTrimmable>true</IsTrimmable>`. Runs in Minimal APIs, background workers, CLI apps, and native C# .NET MAUI ViewModels.
- **`Lux.Agent`** — Multiplatform Blazor Web (Static SSR, Interactive Server, WebAssembly, Auto) & `.NET MAUI Blazor Hybrid` (`browser`, `ios`, `android`, `maccatalyst`, `windows`).

## Highlights

- **Semantic Snapshot, Query & Act** (`Semantic Bridge`) — Exposes up to 100 controls by stable ref (c1, c2…), queries select/filter choices without opening popups, and runs semantic actions (setValue, toggle, submit).
- **Permission Gate & Confirmation Policy** (`Policy Guard`) — Global AllowPolicy or per-call gate with ConfirmRequired flags to approve or block consequential actions before execution.
- **Observe Stream & Secret Redaction** (`Privacy First`) — Unified ObservedChange stream (Agent/User source) where Password and OneTimeCode fields are always redacted as [redacted].
- **WebMCP JS Assets & UndoStack<T>** (`WebMCP + Undo`) — Ships _content/Lux.Agent/lux-agent.js + lux-behaviors.js for WebMCP and a bounded generic UndoStack<T> in Lux.Agent.Core (NativeAOT).

## Installation

```bash
dotnet add package Lux.Agent.Core   # Headless C# / NativeAOT / MAUI C#
dotnet add package Lux.Agent        # Blazor Web & .NET MAUI Blazor Hybrid
```

Register services in `Program.cs` (Blazor Web App) or `MauiProgram.cs` (.NET MAUI Blazor Hybrid):

```csharp
builder.Services.AddLuxAgent();
```

## Quick Start

### Headless Core (`Lux.Agent.Core`)

```csharp
using Blux.UI.Tools;

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

### Blazor Web & .NET MAUI Hybrid (`Lux.Agent`)

```razor
// Served automatically via _content/Lux.Agent/lux-agent.js
const snap = window.Blux.agent.snapshot();
const res  = await window.Blux.agent.act([
  { ref: 'Project name', action: 'setValue', args: ['Atlas'] }
]);

// Privacy-preserving observation stream (passwords auto-masked as "[redacted]")
const stop = window.Blux.agent.observe(evt => console.log(evt));
```

## Documentation

| Guide | Description |
| :--- | :--- |
| [Getting Started](./docs/getting-started.md) | Installation, DI registration, stylesheets, and first working example |
| [Architecture](./docs/architecture.md) | Headless `.Core` vs UI split, NativeAOT guarantees, and platform matrix |
| [API Reference](./docs/api-reference.md) | Exhaustive reference of every public class, enum, property, method, and Razor parameter |
| [MAUI & NativeAOT](./docs/maui-and-aot.md) | `.NET MAUI Blazor Hybrid` (`iOS`, `Android`, `macOS`, `Windows`) and NativeAOT deployment |
| [Recipes & Testing](./docs/recipes.md) | End-to-end production patterns and `xUnit` / `bUnit` testing recipes |
| [AI Agent Skill](./skills/lux-agent/SKILL.md) | Machine-readable guidance for coding agents |

## Key Public Types

| Type | Kind | Package | Summary |
| :--- | :--- | :--- | :--- |
| `AgentChangeSource` | `enum` | `Lux.Agent.Core` | Source of a component state change reported to `AgentBridgeService.Observe`. |
| `ObservedChange` | `record` | `Lux.Agent.Core` | A single notification emitted by the unified `observe` change stream. |
| `AgentComponentSnapshot` | `record` | `Lux.Agent.Core` | Snapshot entry describing one component on the page and the actions it currently supports. |
| `AgentChoiceItem` | `record` | `Lux.Agent.Core` | One choice item returned by `AgentBridgeService.Query` for a select, filter, or list component. |
| `AgentQueryMatch` | `record` | `Lux.Agent.Core` | Result entry returned by `AgentBridgeService.Query`. |
| `AgentQueryRequest` | `class` | `Lux.Agent.Core` | Query parameters for `AgentBridgeService.Query`. |
| `AgentActStep` | `record` | `Lux.Agent.Core` | One action step in an `AgentBridgeService.ActAsync` call. |
| `AgentActStepOutcome` | `record` | `Lux.Agent.Core` | Outcome of a single step in `AgentActResult`. |
| `AgentActResult` | `record` | `Lux.Agent.Core` | Result of an `AgentBridgeService.ActAsync` invocation. |
| `AgentToolDefinition` | `record` | `Lux.Agent.Core` | Tool metadata and hints following the `ui-sdk.dev` Agent / WebMCP specification. |
| `UndoStack<T>` | `class` | `Lux.Agent.Core` | Generic undo/redo snapshot stack inspired by `createUndoStack` from `ui-sdk.dev`. |
| `AgentBridgeService` | `class` | `Lux.Agent` | Semantic Agent Bridge inspired by `ui-sdk.dev` (`createAgent`, `snapshot`, `query`, `act`, `observe`). Allows AI agents and in-app assistants to read component state, query option lists without opening popups, execute semantic actions with permission checks, and subscribe to a unified `ObservedChange` stream with automatic redaction of sensitive fields. |
| `LuxAgentServiceCollectionExtensions` | `class` | `Lux.Agent` | Public API type |

## Building & Testing

```bash
dotnet build
dotnet test
dotnet pack -c Release
```

## License

MIT — see [LICENSE](./LICENSE). Part of the **[LuxStack](../lux.stack/README.md)** ecosystem.
