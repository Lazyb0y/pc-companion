using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Desktop.SystemMeters;

namespace PCCompanion.Modules.Desktop;

/// <summary>
/// Desktop: features that add to the taskbar and the desktop.
/// </summary>
public static class DesktopModule
{
    public const string Id = "desktop";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddDesktopModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeature, SystemMetersFeature>();
        return services;
    }
}
