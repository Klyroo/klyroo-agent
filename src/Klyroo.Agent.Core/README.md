# Klyroo.Agent.Core

The data types of the Klyroo agent bridge and `UndoStack<T>`, with no dependencies. The package is declared
NativeAOT compatible and trimmable.

It contains the records and enum that describe what an AI agent sees and does: `AgentComponentSnapshot`,
`AgentQueryRequest`, `AgentQueryMatch`, `AgentChoiceItem`, `AgentActStep`, `AgentActResult`, `AgentActStepOutcome`,
`ObservedChange`, `AgentChangeSource` and `AgentToolDefinition`. The bridge that produces them,
`AgentBridgeService`, lives in the `Klyroo.Agent` package.

## Install

```bash
dotnet add package Klyroo.Agent.Core --prerelease
```

## Usage

```csharp
using Klyroo.Agent;

var history = new UndoStack<string>("Initial", limit: 50);
history.Record("Updated");
Console.WriteLine(history.Undo()); // Initial
```

See the [repository README](https://github.com/klyroo/klyroo-agent#readme) for the full documentation.
