using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Modules.Windows.MinimizeToTray;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// Window Management: features that arrange and switch windows.
/// </summary>
public static class WindowsModule
{
    public const string Id = "windows";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddWindowsModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeature, AppWindowsFeature>();
        services.AddSingleton<IFeature, MinimizeToTrayFeature>();
        services.AddSingleton<IFeature, HideAppsFeature>();
        return services;
    }
}
