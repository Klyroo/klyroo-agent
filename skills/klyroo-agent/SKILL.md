---
name: klyroo-agent
description: Klyroo.Agent semantic bridge (snapshot, query, act, observe) for .NET controllers, plus a browser runtime with WebMCP registration.
---

# Klyroo.Agent Skill

- Use `AgentBridgeService` (package `Klyroo.Agent`, registered with `AddKlyrooAgent()`) to expose Klyroo.Form, Klyroo.Dashboard and Klyroo.Spreadsheet controllers with `RegisterForm`, `RegisterDashboard` and `RegisterSpreadsheet`.
- Inspect state with `Snapshot()`, list choices without opening popups with `Query()`, run named actions with `ActAsync()`, and watch agent changes with `Observe()`. Steps that need confirmation (`confirmSubmit: true`, spreadsheet `submit` and `clearFile`) are refused until `AllowPolicy` or an `allow` callback decides; set one to approve them. For `IChatClient`, use `AsAIFunctions()` from `Klyroo.Agent.AI`.
- In the browser, import `createAgent` from `/_content/Klyroo.Agent/klyroo-agent.js` for the same operations over the DOM and `registerWebMcp()` for WebMCP. It is independent of `AgentBridgeService`.
- To expose a custom widget to the browser runtime, define it with `Klyroo.definePrimitive` from the Klyroo.UI script; its API methods become agent actions. The headless helpers are on `Klyroo.behaviors`.
- Documentation lives in the repository `docs/` folder, starting at `docs/README.md`.
