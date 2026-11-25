# @klyroo/behaviors

Headless UI behaviors for custom interactive primitives: focus management, selection, popups, reordering, resizing,
viewport pan and zoom, inline editing, an undo stack, and a `definePrimitive` helper that wires them to state.

This package is not published to npm. The same file ships inside the `Klyroo.Agent` NuGet package as
`_content/Klyroo.Agent/klyroo-behaviors.js`; `index.js` here and `src/Klyroo.Agent/wwwroot/klyroo-behaviors.js` are
identical.

The module imports `./shared.js` (`FOCUSABLE`, `ensureId`, `float`, `rovingKey`), which is not part of this
repository, so it cannot be loaded on its own until that file is provided next to it.

## Exports

`announce`, `createUndoStack`, `createRovingFocus`, `createGridFocus`, `createTypeahead`, `createSelection`,
`createPopup`, `createReorder`, `createResize`, `createViewport`, `createInlineEdit`, `definePrimitive`, and the
`KlyrooBehaviors` object that groups them.

A primitive created with `definePrimitive` is discovered by the `klyroo-agent.js` runtime through its
`data-kly-primitive` attribute, and its API methods become agent actions. See the
[concepts page](../../docs/concepts.md#the-browser-runtime).
