using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Klyroo.Agent;

/// <summary>Dependency injection registration for Klyroo.Agent.</summary>
public static class KlyrooAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AgentBridgeService"/> as a scoped service, so each Blazor circuit, HTTP request or
    /// MAUI scope gets its own bridge. Does nothing if the service is already registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddKlyrooAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<AgentBridgeService>();
        return services;
    }
}
