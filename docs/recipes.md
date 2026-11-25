# Recipes & Testing — Klyroo.Agent

## Recipe 1: AllowPolicy Gate & Bounded UndoStack<T>

```csharp
agent.AllowPolicy = (step, confirmRequired) => Task.FromResult(!confirmRequired || userApproved);

var history = new UndoStack<string>("Initial", limit: 50);
history.Record("Updated");
history.Undo(); // restores "Initial" 
```

## Recipe 2: Headless Unit Testing (`xUnit`)

Test all business logic in `Klyroo.Agent.Core` without spinning up a browser or Blazor renderer:

```csharp
using Xunit;

public sealed class AgentUnitTests
{
    [Fact]
    public void Headless_core_runs_without_ui_host()
    {
        // Arrange & Act using Klyroo.Agent.Core directly
        // Assert deterministic outputs with zero JSInterop setup
    }
}
```

## Recipe 3: Component Testing (`bUnit`)

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class AgentComponentTests : BunitContext
{
    public AgentComponentTests()
    {
        Services.AddKlyrooAgent();
    }
}
```

> **Tip**: Always register Klyroo services on `Services` **before** calling `SetRendererInfo(...)` in bUnit tests.
