# Getting Started with Klyroo.Agent

Inspired by ui-sdk.dev: makes Klyroo.Form, Klyroo.Dashboard, and Klyroo.Spreadsheet immediately inspectable and operable by AI agents without brittle CSS selectors. Provides semantic Snapshot/Query/Act, global permission policies, automatic [redacted] secret masking, WebMCP browser assets, and UndoStack<T>.

## 1. Choose Your Package

| Use Case | Package to Install |
| :--- | :--- |
| Headless C#, Minimal APIs, Workers, CLI, or native C# .NET MAUI ViewModels | `Klyroo.Agent.Core` |
| Blazor Web App (Server, WebAssembly, Auto, Static SSR) or `.NET MAUI Blazor Hybrid` | `Klyroo.Agent` |

```bash
dotnet add package Klyroo.Agent.Core
dotnet add package Klyroo.Agent
```

## 2. Register Services

Call `builder.Services.AddKlyrooAgent();` in `Program.cs` (both Server and Client projects when using Blazor Interactive Auto) or `MauiProgram.cs` (after `AddMauiBlazorWebView()`):

```csharp
builder.Services.AddKlyrooAgent();
```

## 3. Add Namespace Imports

In `_Imports.razor`:

```razor
@using Klyroo.UI
@using Klyroo.Agent
@using Klyroo.Form
@using Klyroo.Dashboard
@using Klyroo.Spreadsheet

```

## 4. First Example

### Headless C# (`Klyroo.Agent.Core`)

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

### Blazor / MAUI Hybrid (`Klyroo.Agent`)

```razor
// Served automatically via _content/Klyroo.Agent/klyroo-agent.js
const snap = window.Klyroo.agent.snapshot();
const res  = await window.Klyroo.agent.act([
  { ref: 'Project name', action: 'setValue', args: ['Atlas'] }
]);

// Privacy-preserving observation stream (passwords auto-masked as "[redacted]")
const stop = window.Klyroo.agent.observe(evt => console.log(evt));
```

## Next Steps

- [Architecture & Two-Layer Design](./architecture.md)
- [Complete API Reference](./api-reference.md)
- [.NET MAUI & NativeAOT Guide](./maui-and-aot.md)
- [Recipes & Testing](./recipes.md)
