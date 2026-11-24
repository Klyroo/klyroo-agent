# API Reference — Lux.Agent

Complete public API reference extracted directly from the source code of `Lux.Agent.Core` and `Lux.Agent`.

## C# Types, Controllers, Models & Enums

### `enum AgentChangeSource`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Source of a component state change reported to `AgentBridgeService.Observe`.

| Member | Type | Description |
| :--- | :--- | :--- |
| `Keyboard` | `enum member` | — |
| `Mouse` | `enum member` | — |
| `Touch` | `enum member` | — |
| `Pen` | `enum member` | — |
| `Virtual` | `enum member` | — |
| `Programmatic` | `enum member` | — |
| `Agent` | `enum member` | — |

### `record ObservedChange`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

A single notification emitted by the unified `observe` change stream.

### `record AgentComponentSnapshot`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Snapshot entry describing one component on the page and the actions it currently supports.

### `record AgentChoiceItem`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

One choice item returned by `AgentBridgeService.Query` for a select, filter, or list component.

### `record AgentQueryMatch`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Result entry returned by `AgentBridgeService.Query`.

### `class AgentQueryRequest`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Query parameters for `AgentBridgeService.Query`.

| Member | Type | Description |
| :--- | :--- | :--- |
| `Within` | `string?` | — |
| `Text` | `string?` | — |
| `Kind` | `string?` | — |

### `record AgentActStep`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

One action step in an `AgentBridgeService.ActAsync` call.

### `record AgentActStepOutcome`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Outcome of a single step in `AgentActResult`.

### `record AgentActResult`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Result of an `AgentBridgeService.ActAsync` invocation.

### `record AgentToolDefinition`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Tool metadata and hints following the `ui-sdk.dev` Agent / WebMCP specification.

### `class UndoStack<T>`
*Package*: `Lux.Agent.Core` · *Source*: `src/Lux.Agent.Core/AgentBridgeModels.cs`

Generic undo/redo snapshot stack inspired by `createUndoStack` from `ui-sdk.dev`.

| Member | Type | Description |
| :--- | :--- | :--- |
| `Current` | `T` | — |
| `CanUndo` | `bool` | — |
| `CanRedo` | `bool` | — |
| `Record(T next)` | `T` | — |
| `Undo()` | `T` | — |
| `Redo()` | `T` | — |
| `Clear()` | `void` | — |

### `class AgentBridgeService`
*Package*: `Lux.Agent` · *Source*: `src/Lux.Agent/AgentBridgeService.cs`

Semantic Agent Bridge inspired by `ui-sdk.dev` (`createAgent`, `snapshot`, `query`, `act`, `observe`). Allows AI agents and in-app assistants to read component state, query option lists without opening popups, execute semantic actions with permission checks, and subscribe to a unified `ObservedChange` stream with automatic redaction of sensitive fields.

| Member | Type | Description |
| :--- | :--- | :--- |
| `Ref` | `string` | — |
| `Component` | `string` | — |
| `Label` | `string` | — |
| `GetValue` | `Func<object?>` | — |
| `Actions` | `IReadOnlyList<string>` | — |
| `GetChoices` | `Func<IReadOnlyList<AgentChoiceItem>>` | — |
| `ExecuteAction` | `Func<string, IReadOnlyList<object?>, Task<bool>>` | — |
| `IsDisabled` | `Func<bool>?` | — |
| `IsOffPage` | `Func<bool>?` | — |
| `Sensitive` | `bool` | — |
| `ConfirmRequired` | `bool` | — |
| `AllowPolicy` | `Func<AgentActStep, bool, Task<bool>>?` | Optional global permission gate checked before every `ActAsync` step. |
| `GetToolDefinitions()` | `IReadOnlyList<AgentToolDefinition>` | Returns the three standard `ui-sdk.dev` agent tools (`snapshot`, `query`, `act`). |
| `Observe(Action<ObservedChange> callback)` | `IDisposable` | Subscribes to all component state changes across registered controllers. Returns an `IDisposable` to unsubscribe. |
| `RegisterForm(FormController form, bool confirmSubmit = false)` | `void` | Registers a `FormController` and all its fields with the agent bridge. |
| `RegisterDashboard(DashboardController dashboard)` | `void` | Registers a `DashboardController` (its filters, views, and refresh action) with the agent bridge. |
| `RegisterSpreadsheet(SpreadsheetImporter importer)` | `void` | Registers a `SpreadsheetImporter` with the agent bridge. |
| `Snapshot(bool includeOffPage = true)` | `IReadOnlyList<AgentComponentSnapshot>` | Returns a semantic snapshot of up to 100 registered components, their state, and their available actions. |
| `Query(AgentQueryRequest? request = null)` | `IReadOnlyList<AgentQueryMatch>` | Finds components by label or kind, or queries available choices inside a component without opening popups. |
| `ActAsync(
        IReadOnlyList<AgentActStep> steps,
        Func<AgentActStep, bool, Task<bool>>? allow = null)` | `Task<AgentActResult>` | Executes one or more semantic actions in sequence on registered components by ref or case/accent-insensitive label. |
| `Dispose()` | `void` | — |

### `class LuxAgentServiceCollectionExtensions`
*Package*: `Lux.Agent` · *Source*: `src/Lux.Agent/LuxAgentServiceCollectionExtensions.cs`

| Member | Type | Description |
| :--- | :--- | :--- |
| `AddLuxAgent(this IServiceCollection services)` | `IServiceCollection` | — |
