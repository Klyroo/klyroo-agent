---
name: klyroo-agent
description: Klyroo.Agent semantic bridge (snapshot, query, act, observe) for .NET controllers, plus a browser runtime with WebMCP registration.
---

# Klyroo.Agent Skill

- Use `AgentBridgeService` (package `Klyroo.Agent`, registered with `AddKlyrooAgent()`) to expose Klyroo.Form, Klyroo.Dashboard and Klyroo.Spreadsheet controllers with `RegisterForm`, `RegisterDashboard` and `RegisterSpreadsheet`.
- Inspect state with `Snapshot()`, list choices without opening popups with `Query()`, run named actions with `ActAsync()`, and watch agent changes with `Observe()`. Set `AllowPolicy` to gate consequential actions: `confirmSubmit: true` only flags the form.
- In the browser, import `createAgent` from `/_content/Klyroo.Agent/klyroo-agent.js` for the same operations over the DOM and `registerWebMcp()` for WebMCP. It is independent of `AgentBridgeService`.
- `@klyroo/behaviors` (`definePrimitive`, `createPopup`, `createRovingFocus`, `createSelection`, ...) is not published to npm and imports a `./shared.js` that this repository does not contain.
- Documentation lives in the repository `docs/` folder, starting at `docs/README.md`.
