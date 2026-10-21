using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// App Windows settings, stored in <c>features.appWindows</c>. See docs/features/windows/app-windows.md.
/// </summary>
public sealed record AppWindowsSettings : IFeatureSettings<AppWindowsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The shortcut. A number in the file reads as any value, named or not; an unknown one
    /// registers no shortcut.
    /// </summary>
    public AppWindowsShortcut Shortcut { get; set; } = AppWindowsShortcut.AltAboveTab;

    public bool IncludeMinimized { get; set; }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static AppWindowsSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, AppWindowsJsonContext.Default.AppWindowsSettings, () => new AppWindowsSettings());

    public JsonElement Write() => FeatureSettings.Write(this, AppWindowsJsonContext.Default.AppWindowsSettings);

    public AppWindowsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}
