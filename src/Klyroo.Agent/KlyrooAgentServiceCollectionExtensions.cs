using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Klyroo.Agent;

public static class KlyrooAgentServiceCollectionExtensions
{
    public static IServiceCollection AddKlyrooAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<AgentBridgeService>();
        return services;
    }
}
