using Klyroo.Form;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class ObserverIsolationTests
{
    [Fact]
    public async Task A_throwing_observer_does_not_abort_ActAsync_or_skip_other_observers()
    {
        var agent = new AgentBridgeService();
        var form = new FormController(new FormSchema
        {
            FormKey = "profile",
            Fields =
            [
                new FormFieldSchema { Key = "name", Label = "Name" },
                new FormFieldSchema { Key = "city", Label = "City" },
            ]
        });
        agent.RegisterForm(form);

        using var failing = agent.Observe(_ => throw new InvalidOperationException("observer failure"));
        var observed = new List<ObservedChange>();
        using var recording = agent.Observe(observed.Add);

        var result = await agent.ActAsync(
        [
            new AgentActStep("Name", "setValue", ["Ada"]),
            new AgentActStep("City", "setValue", ["London"]),
        ]);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(2, observed.Count);
        Assert.Equal("Ada", form.GetValue("name"));
        Assert.Equal("London", form.GetValue("city"));
    }
}
