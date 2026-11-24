# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- **Primary**: .NET 10 / Blazor and `.NET MAUI Blazor Hybrid` developers building AI-assisted applications, in-app copilots, WebMCP-enabled web tools, and human-in-the-loop agent workflows.
- **Secondary**: AI agents and LLM tool callers interacting with registered forms, dashboards, and importers through semantic Snapshot/Query/Act APIs.

## Product Purpose

`lux-agent` provides a headless NativeAOT semantic UI bridge (`Lux.Agent.Core`) and Blazor/WebMCP integration (`Lux.Agent`) that lets AI agents inspect, query, observe, and safely operate `Lux.Form`, `Lux.Dashboard`, and `Lux.Spreadsheet` controls without brittle CSS selectors or DOM scraping.

Success means an application registers its existing controllers once (`RegisterForm`, `RegisterDashboard`, `RegisterSpreadsheet`) and immediately gains stable semantic refs (`c1`, `c2`…), choice querying, a unified `Observe` audit stream, automatic `[redacted]` masking of secrets, `UndoStack<T>` rollback, and an `AllowPolicy` confirmation gate for consequential actions.

## Positioning

Unlike browser-automation wrappers that guess fragile CSS selectors or expose raw DOM secrets to LLMs, `Lux.Agent` binds directly to strongly typed C# domain controllers (`FormController`, `DashboardController`, `SpreadsheetImporter`), guarantees `[redacted]` secret masking by design, enforces human-in-the-loop `AllowPolicy` confirmation gates, and works identically in headless NativeAOT C# and Blazor/WebMCP browsers under MIT.

## Operating Context

- **Packages**: `Lux.Agent.Core` (`AgentActStep`, `AgentChoiceItem`, `ObservedChange`, `UndoStack<T>`, semantic contracts) and `Lux.Agent` (`AgentBridgeService`, WebMCP browser assets `lux-agent.js` / `lux-behaviors.js`).
- **Integration**: Connects directly to `Lux.Form`, `Lux.Dashboard`, and `Lux.Spreadsheet`.
- **Showcase**: Powers the live `Lux.Agent Copilot` sidebar in `Lux.Stack.Site` `/showcase` with `Snapshot`, `Act`, `Observe` audit log, `[redacted]` webhook secret proof, `UndoStack<T>`, and the `AllowPolicy` human approval modal.

## Capabilities and Constraints

- `Lux.Agent.Core` is 100% headless with `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>`.
- Exposes up to 100 registered controls per bridge by stable semantic ref (`c1`, `c2`…) and disambiguated label.
- `Password` and `OneTimeCode` fields must always serialize as `[redacted]` in `Snapshot()` and `Observe()` — never raw values.
- Consequential actions (`confirmSubmit: true` or custom `AllowPolicy`) must pause cleanly for explicit user approval and resolve safely on refusal or dialog dismissal without hanging tasks.
- 100% MIT open-source license; strict `Lux.Agent*` / `lux-agent.js` naming.

## Brand Commitments

- **Name**: `Lux.Agent` (`lux-agent`), part of the **LuxStack** ecosystem.
- **Voice**: Trustworthy, transparent, and safety-first around AI-initiated mutations.

## Evidence on Hand

- Passing unit tests (`tests/Lux.Agent.Tests`) covering Snapshot, Query, Act, `AllowPolicy`, `[redacted]` secret masking, `Observe`, and `UndoStack<T>`, plus NativeAOT publish verification in CI.
- Live interactive Copilot sidebar with `AllowPolicy` approval modal and real-time `Observe` audit log in `Lux.Stack.Site` `/showcase` and product docs at `/agent`.
- **Explicit absences (do not fabricate)**: No NuGet download stats, customer logos, or third-party testimonials exist yet.

## Product Principles

1. **Semantic controls over fragile selectors**: Agents act on typed controller capabilities (`setValue`, `toggle`, `resetFilters`, `submit`), never DOM structure guesses.
2. **Privacy by default**: Secrets (`Password`, `OneTimeCode`) are redacted unconditionally before entering any snapshot or observe stream.
3. **Human-in-the-loop for consequential actions**: Mutations like form submission can always be gated behind explicit confirmation via `AllowPolicy`.
4. **Observable and reversible**: Every agent or user mutation emits a structured `ObservedChange` event and pairs cleanly with `UndoStack<T>`.

## Accessibility & Inclusion

- Durable standard: **WCAG 2.1 AA** for all agent-facing UI surfaces (copilot panels, approval modals, audit logs): keyboard-operable approval/refusal dialogs with safe `Escape` dismissal, clear attribution of agent vs. user changes, and readable audit feedback.
