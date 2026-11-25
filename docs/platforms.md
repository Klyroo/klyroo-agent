# Platforms

## What the csproj files declare

| Property | `Klyroo.Agent.Core` | `Klyroo.Agent` |
| :--- | :--- | :--- |
| Target framework | `net10.0` | `net10.0` |
| `IsAotCompatible` | `true` | not set |
| `IsTrimmable` | `true` | `true` |
| `SupportedPlatform` | none declared | `browser`, `ios`, `android`, `maccatalyst`, `windows` |
| Dependencies | none | `Microsoft.AspNetCore.Components.Web`, `Klyroo.Agent.Core`, `Klyroo.Form.Core`, `Klyroo.Dashboard.Core`, `Klyroo.Spreadsheet.Core` |

Both projects build with warnings as errors, so the trimming and AOT analyzers that apply to them are clean.

## Blazor render modes

`AgentBridgeService` uses no JavaScript interop, no `IJSRuntime`, no component lifecycle and no browser API, so it
does not depend on the render mode. What differs is how long the scoped service and your controllers live:

| Render mode | Lifetime of the service and controllers | Note |
| :--- | :--- | :--- |
| Static SSR | One HTTP request | Useful for server-side tool handlers; there is no live UI to observe. |
| Interactive Server | One circuit | The agent edits the same controller instances the page renders. |
| Interactive WebAssembly | One browser tab | The bridge runs in the browser, next to the controllers. |
| Auto | Server first, then the browser | Register `AddKlyrooAgent()` in both projects. Controllers on the server and in the browser are different instances. |

An agent that runs on your server and must edit what a browser shows has to reach the controllers of that user's
session, which is your application's responsibility, for example through a service that holds the user's
`AgentBridgeService`.

## .NET MAUI Blazor Hybrid

Register the bridge after `AddMauiBlazorWebView()`:

```csharp
builder.Services.AddMauiBlazorWebView();
builder.Services.AddKlyrooAgent();
```

`Klyroo.Agent` declares `ios`, `android`, `maccatalyst` and `windows` as supported platforms, so the platform
compatibility analyzer does not warn when you reference it from a MAUI project. The service itself runs in the .NET
runtime of the app, not in the WebView.

If your MAUI app uses XAML or C# Markup instead of Blazor, note that `AgentBridgeService` has no WebView
dependency in its code, but this repository does not test that setup. `Klyroo.Agent.Core` alone gives you only the types.

## NativeAOT and trimming

`Klyroo.Agent.Core` declares `IsAotCompatible` and `IsTrimmable`. It contains records, an enum and
`UndoStack<T>`, with no reflection.

`Klyroo.Agent` declares `IsTrimmable` but not `IsAotCompatible`, so the AOT analyzers are not enforced for it. Its
service code uses LINQ, lambdas and no reflection of its own, but it depends on the Klyroo Core packages, and you
should publish with `PublishAot` and read the warnings before relying on it in an AOT app. The tool handler in the
[guides](guides.md#expose-the-operations-as-llm-tools) serializes nothing itself; if you serialize its results in an
AOT app, use a source-generated `System.Text.Json` context.

## The browser runtime

`klyroo-agent.js` runs in any browser that supports ES modules. It uses `CSS.escape`, custom events and standard
DOM APIs. WebMCP registration needs a browser that exposes `document.modelContext` or `navigator.modelContext` with
`registerTool`; without it the call returns a no-op function and nothing is registered.

Static web assets are served at `/_content/Klyroo.Agent/` in development and when your app maps static assets in
production. If the script returns 404 in a published app, check that your app calls `UseStaticFiles` or
`MapStaticAssets`.
