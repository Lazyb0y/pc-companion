using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Hide Apps settings, stored in <c>features.hideApps</c>. See docs/features/windows/hide-apps.md.
/// </summary>
public sealed record HideAppsSettings : IFeatureSettings<HideAppsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The Hide app shortcut. A number in the file reads as any value, named or not; an unknown
    /// one registers no shortcut.
    /// </summary>
    public HideAppShortcut HideAppShortcut { get; set; } = HideAppShortcut.WindowsControlH;

    /// <summary>The Hide others shortcut, read as <see cref="HideAppShortcut"/> is.</summary>
    public HideOthersShortcut HideOthersShortcut { get; set; } = HideOthersShortcut.WindowsControlShiftH;

    /// <summary>Whether Hide others leaves windows on other displays alone.</summary>
    public bool FrontDisplayOnly { get; set; }

    /// <summary>Whether an app's other minimized windows come back with the one that comes back.</summary>
    public bool BringBackWholeApp { get; set; } = true;

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static HideAppsSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, HideAppsJsonContext.Default.HideAppsSettings, () => new HideAppsSettings());

    public JsonElement Write() => FeatureSettings.Write(this, HideAppsJsonContext.Default.HideAppsSettings);

    public HideAppsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}
