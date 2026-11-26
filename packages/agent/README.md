# @klyroo/agent

The browser runtime of Klyroo.Agent: `snapshot`, `query`, `act` and `observe` over the DOM, with optional WebMCP
registration. It is an ES module with no imports.

This package is not published to npm. The same file ships inside the `Klyroo.Agent` NuGet package and is served at
`/_content/Klyroo.Agent/klyroo-agent.js`; `index.js` here and `src/Klyroo.Agent/wwwroot/klyroo-agent.js` are identical.
It does not talk to the .NET `AgentBridgeService`.

## Usage

```html
<script type="module">
  import { createAgent } from '/_content/Klyroo.Agent/klyroo-agent.js';

  const agent = createAgent();
  console.log(agent.snapshot());
  await agent.act({ steps: [{ ref: 'Project name', action: 'setValue', args: ['Atlas'] }] });
  const unregister = agent.registerWebMcp();
</script>
```

`act` refuses disabled and hidden elements, refuses `data-agent-confirm` elements unless an `allow` policy is set, and
returns `{ ok: false, error }` instead of throwing. Run the tests with `npm test` in this folder (Node 22, no
dependencies).

Exports: `createAgent`, `observe`, `emitObservedChange`, `labelOf`, `agentTools` and `registerWebMcp`. See the
[guide](../../docs/guides.md#use-the-browser-runtime-and-webmcp) and the
[concepts page](../../docs/concepts.md#the-browser-runtime).
