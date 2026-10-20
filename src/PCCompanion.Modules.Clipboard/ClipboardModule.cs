using Microsoft.Extensions.DependencyInjection;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Clipboard.ClipboardActions;

namespace PCCompanion.Modules.Clipboard;

/// <summary>
/// Clipboard &amp; Text: features that act on copied and typed text.
/// </summary>
public static class ClipboardModule
{
    public const string Id = "clipboard";

    /// <summary>
    /// Registers the module's features with the host.
    /// </summary>
    public static IServiceCollection AddClipboardModule(this IServiceCollection services)
    {
        services.AddSingleton<IFeature, ClipboardActionsFeature>();
        return services;
    }
}
