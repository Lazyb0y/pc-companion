using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Title Bar Actions settings, stored in <c>features.titleBarActions</c>. See docs/features/windows/title-bar-actions.md.
/// </summary>
public sealed record TitleBarActionsSettings : IFeatureSettings<TitleBarActionsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>
    /// What a middle-click on a title bar does. A number in the file reads as any value, named or
    /// not; an unknown one does nothing.
    /// </summary>
    public TitleBarClickAction MiddleClick { get; set; } = TitleBarClickAction.Minimize;

    public TitleBarClickAction ShiftMiddleClick { get; set; }

    public TitleBarClickAction ControlMiddleClick { get; set; }

    /// <summary>What the wheel on a title bar does.</summary>
    public TitleBarWheelAction Wheel { get; set; }

    public TitleBarWheelAction ControlWheel { get; set; }

    /// <summary>The apps that keep their title-bar clicks: their title bars get no actions.</summary>
    public IReadOnlyList<ListedApp> ExcludedApps { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A blank app ID or
    /// program counts as none, and a listed app without an app ID or a program, or with both, is
    /// left out, as is a null in the list and an app listed a second time.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static TitleBarActionsSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, TitleBarActionsJsonContext.Default.TitleBarActionsSettings, () => new TitleBarActionsSettings());

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

    public JsonElement Write() => FeatureSettings.Write(this, TitleBarActionsJsonContext.Default.TitleBarActionsSettings);

    public TitleBarActionsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => ExcludedApps.Any(listed => listed.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, unless it's listed already
    /// or has neither or both of an app ID and a program.
    /// </summary>
    public TitleBarActionsSettings WithExcludedApp(ListedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return !app.IsValid || Lists(app) ? this : this with { ExcludedApps = [.. ExcludedApps, app] };
    }

    /// <summary>The settings without the listed app at <paramref name="index"/>.</summary>
    public TitleBarActionsSettings WithoutExcludedApp(int index) =>
        this with { ExcludedApps = [.. ExcludedApps.Where((_, at) => at != index)] };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(TitleBarActionsSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && MiddleClick == other.MiddleClick
        && ShiftMiddleClick == other.ShiftMiddleClick
        && ControlMiddleClick == other.ControlMiddleClick
        && Wheel == other.Wheel
        && ControlWheel == other.ControlWheel
        && ExcludedApps.SequenceEqual(other.ExcludedApps);

    public override int GetHashCode() => HashCode.Combine(Enabled, MiddleClick, ShiftMiddleClick, ControlMiddleClick, Wheel, ControlWheel, ExcludedApps.Count);
}

/// <summary>What a middle-click on a title bar does.</summary>
public enum TitleBarClickAction
{
    /// <summary>Nothing: the click reaches the app.</summary>
    None,

    Minimize,

    /// <summary>Puts the window behind every other window.</summary>
    SendToBack,

    /// <summary>Sends the window to the notification area, through Minimize to Tray.</summary>
    MinimizeToTray,

    /// <summary>Minimizes every window of the window's app, through Hide Apps.</summary>
    HideApp,
}

/// <summary>What the wheel on a title bar does.</summary>
public enum TitleBarWheelAction
{
    /// <summary>Nothing: the wheel reaches the app.</summary>
    None,

    /// <summary>Changes the window's opacity.</summary>
    Opacity,
}
