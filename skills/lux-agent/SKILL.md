---
name: lux-agent
description: LuxStack composable UI behaviors (@lux/behaviors) and Semantic Agent Bridge (snapshot, query, act, observe, WebMCP).
---

# Lux.Agent Skill

- Use `@lux/behaviors` (`definePrimitive`, `createPopup`, `createRovingFocus`, `createGridFocus`, `createSelection`, `createReorder`, `createResize`, `createViewport`, `createInlineEdit`, `createUndoStack`) to assemble custom interactive UI primitives.
- Use `Blux.agent` (JS) or `AgentBridgeService` (.NET) to inspect page state via `snapshot()`, search choices without opening popups via `query()`, execute semantic actions via `act()`, and stream changes via `observe()`.
