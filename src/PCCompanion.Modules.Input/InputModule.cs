using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Modules.Input.LockKeys;
using PCCompanion.Modules.Input.Notice;

namespace PCCompanion.Modules.Input;

/// <summary>
/// Input &amp; Triggers: features that respond to the mouse and keyboard.
/// </summary>
public static class InputModule
{
    public const string Id = "input";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddInputModule(this IServiceCollection services)
    {
        services.AddSingleton<SharedNotice>();
        services.AddSingleton<IFeature, HotCornersFeature>();
        services.AddSingleton<IFeature, LockKeysFeature>();
        services.AddSingleton<IFeature, InputLanguagePerAppFeature>();
        return services;
    }
}
