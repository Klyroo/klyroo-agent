using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class DashboardViewTests
{
    private static (AgentBridgeService Agent, DashboardController Dashboard) Setup()
    {
        var dashboard = new DashboardController("sales");
        dashboard.AddView("overview", "Overview");
        dashboard.AddView("cohorts", "Cohorts");
        dashboard.AddView("beta", "Beta", enabled: false);
        var agent = new AgentBridgeService();
        agent.RegisterDashboard(dashboard);
        return (agent, dashboard);
    }

    [Fact]
    public async Task SetView_selects_an_enabled_view()
    {
        var (agent, dashboard) = Setup();
        var result = await agent.ActAsync([new AgentActStep("sales", "setView", ["cohorts"])]);
        Assert.True(result.Ok, result.Error);
        Assert.Equal("cohorts", dashboard.CurrentView);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("beta")]
    [InlineData("Cohorts")]
    public async Task SetView_fails_for_an_unknown_or_disabled_view_and_keeps_the_current_one(string key)
    {
        var (agent, dashboard) = Setup();
        var before = dashboard.CurrentView;

        var result = await agent.ActAsync([new AgentActStep("sales", "setView", [key])]);

        Assert.False(result.Ok);
        Assert.Contains("not an enabled view", result.Error);
        Assert.Equal(before, dashboard.CurrentView);
    }

    [Fact]
    public async Task SetView_without_a_key_fails_with_a_reason()
    {
        var (agent, _) = Setup();
        var result = await agent.ActAsync([new AgentActStep("sales", "setView")]);
        Assert.False(result.Ok);
        Assert.Contains("key of the view", result.Error);
    }
}
