# Getting Started with Lux.Agent

Inspired by ui-sdk.dev: makes Lux.Form, Lux.Dashboard, and Lux.Spreadsheet immediately inspectable and operable by AI agents without brittle CSS selectors. Provides semantic Snapshot/Query/Act, global permission policies, automatic [redacted] secret masking, WebMCP browser assets, and UndoStack<T>.

## 1. Choose Your Package

| Use Case | Package to Install |
| :--- | :--- |
| Headless C#, Minimal APIs, Workers, CLI, or native C# .NET MAUI ViewModels | `Lux.Agent.Core` |
| Blazor Web App (Server, WebAssembly, Auto, Static SSR) or `.NET MAUI Blazor Hybrid` | `Lux.Agent` |

```bash
dotnet add package Lux.Agent.Core
dotnet add package Lux.Agent
```

## 2. Register Services

Call `builder.Services.AddLuxAgent();` in `Program.cs` (both Server and Client projects when using Blazor Interactive Auto) or `MauiProgram.cs` (after `AddMauiBlazorWebView()`):

```csharp
builder.Services.AddLuxAgent();
```

## 3. Add Namespace Imports

In `_Imports.razor`:

```razor
@using Blux.UI
@using Blux.UI.Tools

```

## 4. First Example

### Headless C# (`Lux.Agent.Core`)

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

### Blazor / MAUI Hybrid (`Lux.Agent`)

```razor
// Served automatically via _content/Lux.Agent/lux-agent.js
const snap = window.Blux.agent.snapshot();
const res  = await window.Blux.agent.act([
  { ref: 'Project name', action: 'setValue', args: ['Atlas'] }
]);

// Privacy-preserving observation stream (passwords auto-masked as "[redacted]")
const stop = window.Blux.agent.observe(evt => console.log(evt));
```

## Next Steps

- [Architecture & Two-Layer Design](./architecture.md)
- [Complete API Reference](./api-reference.md)
- [.NET MAUI & NativeAOT Guide](./maui-and-aot.md)
- [Recipes & Testing](./recipes.md)
