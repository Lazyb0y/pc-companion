using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// End App settings, stored in <c>features.endApp</c>. See docs/features/windows/end-app.md.
/// </summary>
public sealed record EndAppSettings : IFeatureSettings<EndAppSettings>
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The shortcut that starts choosing an app. A number in the file reads as any value, named or
    /// not; an unknown one registers no shortcut.
    /// </summary>
    public EndAppShortcut Shortcut { get; set; } = EndAppShortcut.ControlAltEscape;

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static EndAppSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, EndAppJsonContext.Default.EndAppSettings, () => new EndAppSettings());

    public JsonElement Write() => FeatureSettings.Write(this, EndAppJsonContext.Default.EndAppSettings);

    public EndAppSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}
