using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.HotCorners;

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
        services.AddSingleton<IFeature, HotCornersFeature>();
        return services;
    }
}
