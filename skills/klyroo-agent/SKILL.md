---
name: klyroo-agent
description: Klyroo composable UI behaviors (@klyroo/behaviors) and Semantic Agent Bridge (snapshot, query, act, observe, WebMCP).
---

# Klyroo.Agent Skill

- Use `@klyroo/behaviors` (`definePrimitive`, `createPopup`, `createRovingFocus`, `createGridFocus`, `createSelection`, `createReorder`, `createResize`, `createViewport`, `createInlineEdit`, `createUndoStack`) to assemble custom interactive UI primitives.
- Use `Klyroo.agent` (JS) or `AgentBridgeService` (.NET) to inspect page state via `snapshot()`, search choices without opening popups via `query()`, execute semantic actions via `act()`, and stream changes via `observe()`.
