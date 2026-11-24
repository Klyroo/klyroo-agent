# Lux.Agent (LuxStack)

TanStack AI & Vercel `ui-sdk.dev` equivalent for .NET, Blazor, .NET MAUI, and Web Components:
- **`Lux.Agent.Core`**: Headless C# Semantic Agent models (`ObservedChange`, `AgentComponentSnapshot`, `AgentQueryRequest`, `AgentActStep`, `AgentToolDefinition`, `UndoStack<T>`).
- **`Lux.Agent`**: `AgentBridgeService` connecting `Lux.Form`, `Lux.Dashboard`, `Lux.Spreadsheet` and WebMCP (`document.modelContext.registerTool`) with automatic secret redaction and permission gates (`AddLuxAgent()`).
- **`packages/behaviors` (`@lux/behaviors`)**: Composable headless UI behaviors (`definePrimitive`, `createRovingFocus`, `createGridFocus`, `createTypeahead`, `createSelection`, `createPopup`, `createReorder`, `createResize`, `createViewport`, `createInlineEdit`, `createUndoStack`, `announce`).
- **`packages/agent` (`@lux/agent`)**: Browser DOM agent runtime (`snapshot`, `query`, `act`, `observe`, `registerWebMcp`).
