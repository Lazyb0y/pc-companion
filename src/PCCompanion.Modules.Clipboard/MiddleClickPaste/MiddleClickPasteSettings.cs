using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>
/// Middle-Click Paste settings, stored in <c>features.middleClickPaste</c>. See docs/features/clipboard/middle-click-paste.md.
/// </summary>
public sealed record MiddleClickPasteSettings : IFeatureSettings<MiddleClickPasteSettings>
{
    public bool Enabled { get; set; }

    /// <summary>The apps that keep their middle-click: nothing is pasted in their windows.</summary>
    public IReadOnlyList<ListedApp> ExcludedApps { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A blank app ID or
    /// program counts as none, and a listed app without an app ID or a program, or with both, is
    /// left out, as is a null in the list and an app listed a second time.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static MiddleClickPasteSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, MiddleClickPasteJsonContext.Default.MiddleClickPasteSettings, () => new MiddleClickPasteSettings());

        // The JSON reader checks nullable annotations on properties but not on a list's items,
        // so a null in the file's list of apps reads as one.
        var apps = new List<ListedApp>();
        foreach (var app in settings.ExcludedApps.Where(app => app is not null).Select(app => app.Checked()))
        {
            if (app.IsValid && !apps.Any(listed => listed.IsSameAppAs(app)))
            {
                apps.Add(app);
            }
        }

        return settings with { ExcludedApps = apps };
    }

    public JsonElement Write() => FeatureSettings.Write(this, MiddleClickPasteJsonContext.Default.MiddleClickPasteSettings);

    public MiddleClickPasteSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => ExcludedApps.Any(listed => listed.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, unless it's listed already
    /// or has neither or both of an app ID and a program.
    /// </summary>
    public MiddleClickPasteSettings WithExcludedApp(ListedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return !app.IsValid || Lists(app) ? this : this with { ExcludedApps = [.. ExcludedApps, app] };
    }

    /// <summary>The settings without the listed app at <paramref name="index"/>.</summary>
    public MiddleClickPasteSettings WithoutExcludedApp(int index) =>
        this with { ExcludedApps = [.. ExcludedApps.Where((_, at) => at != index)] };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(MiddleClickPasteSettings? other) =>
        other is not null && Enabled == other.Enabled && ExcludedApps.SequenceEqual(other.ExcludedApps);

    public override int GetHashCode() => HashCode.Combine(Enabled, ExcludedApps.Count);
}
