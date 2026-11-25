using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Modules.Windows.EndApp;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Modules.Windows.WindowShading;
using PCCompanion.Modules.Windows.WindowTabs;

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
        services.AddSingleton<HideAppsMinimized>();
        services.AddSingleton<DropDownWindows>();
        services.AddSingleton<WindowRequests>();
        services.AddSingleton<WindowTabsWindows>();
        services.AddSingleton<ShadedWindows>();
        services.AddSingleton<IFeature, AppWindowsFeature>();
        services.AddSingleton<IFeature, MinimizeToTrayFeature>();
        services.AddSingleton<IFeature, HideAppsFeature>();
        services.AddSingleton<IFeature, DropDownAppsFeature>();
        services.AddSingleton<IFeature, WindowTabsFeature>();
        services.AddSingleton<IFeature, WindowRulesFeature>();
        services.AddSingleton<IFeature, WindowShadingFeature>();
        services.AddSingleton<IFeature, TitleBarActionsFeature>();
        services.AddSingleton<IFeature, EndAppFeature>();
        return services;
    }
}
