using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Display.DisplayProfiles;

namespace PCCompanion.Modules.Display;

/// <summary>
/// Display: features that change the displays' setup.
/// </summary>
public static class DisplayModule
{
    public const string Id = "display";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddDisplayModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeature, DisplayProfilesFeature>();
        return services;
    }
}
