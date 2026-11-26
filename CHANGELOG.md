# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Removed

- `klyroo-behaviors.js` and the `@klyroo/behaviors` source. The module imported a `./shared.js` that was never shipped, so it could not load. The same helpers are in the Klyroo.UI script as `Klyroo.behaviors` and `Klyroo.definePrimitive`.

### Fixed

- An exception thrown by an `Observe` callback escaped `ActAsync` after the step was applied, ending the run and skipping the other subscribers. It is now ignored.

## [0.1.0-preview.1] - 2026-11-24

### Added

- `AgentBridgeService` with `Snapshot`, `Query` and `ActAsync`: list registered components with stable references, list the choices of a select or filter without opening a popup, and run named actions by reference or label, stopping at the first failing step.
- Registration of Klyroo.Form forms (including stepped forms), Klyroo.Dashboard dashboards and Klyroo.Spreadsheet importers with `RegisterForm`, `RegisterDashboard` and `RegisterSpreadsheet`.
- Permission gating with `AllowPolicy`, a per-call `allow` callback and confirmation-required forms.
- Redaction of password and one-time-code values in snapshots, results and observed changes.
- `Observe` change stream delivering an `ObservedChange` for each successful step, and `GetToolDefinitions` describing the `snapshot`, `query` and `act` tools with read-only and consequential hints.
- `AddKlyrooAgent()` dependency injection registration (scoped).
- `UndoStack<T>`, a bounded undo and redo history, in the dependency-free, NativeAOT-compatible `Klyroo.Agent.Core` package.
- Browser runtime `klyroo-agent.js` (snapshot, query, act, observe and WebMCP registration) shipped as a static web asset of `Klyroo.Agent`.

[Unreleased]: https://github.com/klyroo/klyroo-agent/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/klyroo/klyroo-agent/releases/tag/v0.1.0-preview.1
