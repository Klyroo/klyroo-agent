using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lux.Agent;

public static class LuxAgentServiceCollectionExtensions
{
    public static IServiceCollection AddLuxAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<AgentBridgeService>();
        return services;
    }
}
