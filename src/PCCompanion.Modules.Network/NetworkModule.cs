using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Network.NetworkProfiles;

namespace PCCompanion.Modules.Network;

/// <summary>
/// Network: features that change the PC's network settings.
/// </summary>
public static class NetworkModule
{
    public const string Id = "network";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddNetworkModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeature, NetworkProfilesFeature>();
        return services;
    }
}
