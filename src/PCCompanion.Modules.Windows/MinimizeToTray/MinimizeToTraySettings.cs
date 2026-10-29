using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// Minimize to Tray settings, stored in <c>features.minimizeToTray</c>. See docs/features/windows/minimize-to-tray.md.
/// </summary>
public sealed record MinimizeToTraySettings : IFeatureSettings<MinimizeToTraySettings>
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The shortcut. A number in the file reads as any value, named or not; an unknown one
    /// registers no shortcut.
    /// </summary>
    public MinimizeToTrayShortcut Shortcut { get; set; } = MinimizeToTrayShortcut.WindowsShiftZ;

    /// <summary>Whether Shift + Minimize sends a window to the tray.</summary>
    public bool ShiftMinimize { get; set; } = true;

    /// <summary>The apps whose windows go to the tray whenever they're minimized.</summary>
    public IReadOnlyList<MinimizedApp> MinimizedApps { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A blank app ID or
    /// program counts as none, and a listed app without an app ID or a program, or with both, is
    /// left out, as is a null in the list.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static MinimizeToTraySettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, MinimizeToTrayJsonContext.Default.MinimizeToTraySettings, () => new MinimizeToTraySettings());

        // The JSON reader checks nullable annotations on properties but not on a list's items,
        // so a null in the file's list of apps reads as one.
        return settings with
        {
            MinimizedApps = [.. settings.MinimizedApps.Where(app => app is not null).Select(app => app.Checked()).Where(app => app.IsValid)],
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, MinimizeToTrayJsonContext.Default.MinimizeToTraySettings);

    public MinimizeToTraySettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(MinimizedApp app) => MinimizedApps.Any(listed => listed.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, unless it's listed already
    /// or has neither or both of an app ID and a program.
    /// </summary>
    public MinimizeToTraySettings WithMinimizedApp(MinimizedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return !app.IsValid || Lists(app) ? this : this with { MinimizedApps = [.. MinimizedApps, app] };
    }

    /// <summary>The settings without the listed app at <paramref name="index"/>.</summary>
    public MinimizeToTraySettings WithoutMinimizedApp(int index) =>
        this with { MinimizedApps = [.. MinimizedApps.Where((_, at) => at != index)] };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(MinimizeToTraySettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && Shortcut == other.Shortcut
        && ShiftMinimize == other.ShiftMinimize
        && MinimizedApps.SequenceEqual(other.MinimizedApps);

    public override int GetHashCode() => HashCode.Combine(Enabled, Shortcut, ShiftMinimize, MinimizedApps.Count);
}
