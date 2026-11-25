# Architecture — Klyroo.Agent

Klyroo.Agent follows the strict headless two-layer architecture shared across every repository in **Klyroo**:

```text
+-------------------------------------------------------------------+
|  Klyroo.Agent                                                       |
|  Blazor Web (SSR / Server / WASM / Auto) & .NET MAUI Hybrid       |
|  SupportedPlatform: browser, ios, android, maccatalyst, windows   |
+-------------------------------------------------------------------+
                                |
                                v
+-------------------------------------------------------------------+
|  Klyroo.Agent.Core                                                  |
|  100% Headless C# · Zero UI Dependency · IsAotCompatible=true     |
|  Works in APIs, Workers, CLI, NativeAOT & Native C# MAUI          |
+-------------------------------------------------------------------+
```

## Design Principles

1. **Zero UI Coupling in `Klyroo.Agent.Core`** — State machines, validation rules, ASTs, formatters, and parsers live in `Klyroo.Agent.Core` so they can be unit tested in milliseconds with plain `xUnit` without spinning up a renderer.
2. **NativeAOT & Trimming Verified** — `Klyroo.Agent.Core` sets `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>` with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
3. **Every Render Mode & Native Hybrid** — `Klyroo.Agent` avoids browser-only assumptions during prerendering and declares `<SupportedPlatform>` for `browser`, `ios`, `android`, `maccatalyst`, and `windows`.
4. **Composable with `Klyroo.Stack`** — Install `Klyroo.Agent` standalone or aggregate the entire ecosystem with `dotnet add package Klyroo.Stack` and `services.AddKlyrooStack()`.

## Supported Platforms

- Headless C# / NativeAOT
- Blazor Server / WASM / Auto
- .NET MAUI Blazor Hybrid
- Browser WebMCP (navigator.modelContext)
