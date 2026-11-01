using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// Drop-down Apps settings, stored in <c>features.dropDownApps</c>. See docs/features/windows/drop-down-apps.md.
/// </summary>
public sealed record DropDownAppsSettings : IFeatureSettings<DropDownAppsSettings>
{
    public const int MinimumWidth = 30;
    public const int MinimumHeight = 20;
    public const int MaximumSize = 100;

    public bool Enabled { get; set; }

    /// <summary>The listed apps, each with its shortcut.</summary>
    public IReadOnlyList<DropDownApp> Apps { get; set; } = [];

    /// <summary>The display a window drops down on.</summary>
    public DropDownDisplay Display { get; set; } = DropDownDisplay.WithPointer;

    /// <summary>The window's width, in percent of the work area's width.</summary>
    public int Width { get; set; } = MaximumSize;

    /// <summary>The window's height, in percent of the work area's height.</summary>
    public int Height { get; set; } = 50;

    /// <summary>Whether the window slides in and out, when Windows' animation effects are on.</summary>
    public bool Slide { get; set; } = true;

    /// <summary>When a shown window hides because another app came to the front.</summary>
    public HideWhenInactive HideWhenInactive { get; set; } = HideWhenInactive.AnyDisplay;

    /// <summary>Whether a shown window is put on top.</summary>
    public bool KeepOnTop { get; set; }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. Listed apps are read
    /// as Hot Corners reads its list: a blank app ID or program counts as none, and an app with
    /// neither or both, or a null, is left out, as is an app listed a second time. Shortcuts are
    /// kept as the file has them; <see cref="DropDownShortcuts.Assign"/> decides which apply. An
    /// unknown display or choice for hiding is the default, and a width or height out of range is
    /// brought into range.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static DropDownAppsSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, DropDownAppsJsonContext.Default.DropDownAppsSettings, () => new DropDownAppsSettings());

        // The JSON reader checks nullable annotations on properties but not on a list's items, so a
        // null in the file's list of apps reads as one.
        var apps = new List<DropDownApp>();
        foreach (var app in settings.Apps.Where(app => app is not null).Select(Checked))
        {
            if (app.App.IsValid && !apps.Any(listed => listed.App.IsSameAppAs(app.App)))
            {
                apps.Add(app);
            }
        }

        // A number in the file reads as any enum value, named or not.
        return settings with
        {
            Apps = apps,
            Display = Enum.IsDefined(settings.Display) ? settings.Display : DropDownDisplay.WithPointer,
            Width = Math.Clamp(settings.Width, MinimumWidth, MaximumSize),
            Height = Math.Clamp(settings.Height, MinimumHeight, MaximumSize),
            HideWhenInactive = Enum.IsDefined(settings.HideWhenInactive) ? settings.HideWhenInactive : HideWhenInactive.AnyDisplay,
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, DropDownAppsJsonContext.Default.DropDownAppsSettings);

    public DropDownAppsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => Apps.Any(listed => listed.App.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, with the first shortcut no
    /// other app has, unless it's listed already or has neither or both of an app ID and a program.
    /// </summary>
    public DropDownAppsSettings WithApp(ListedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return !app.IsValid || Lists(app)
            ? this
            : this with { Apps = [.. Apps, DropDownApp.For(app, DropDownShortcuts.FirstFree(Apps.Select(listed => listed.Shortcut)))] };
    }

    /// <summary>The settings without the listed app at <paramref name="index"/>.</summary>
    public DropDownAppsSettings WithoutApp(int index) =>
        this with { Apps = [.. Apps.Where((_, at) => at != index)] };

    /// <summary>
    /// The settings with the listed app at <paramref name="index"/> dropping down with
    /// <paramref name="shortcut"/>; another app with that shortcut gets None.
    /// </summary>
    public DropDownAppsSettings WithShortcutAt(int index, DropDownShortcut shortcut) =>
        this with
        {
            Apps =
            [
                .. Apps.Select((app, at) =>
                    at == index ? app with { Shortcut = shortcut }
                    : shortcut != DropDownShortcut.None && app.Shortcut == shortcut ? app with { Shortcut = DropDownShortcut.None }
                    : app),
            ],
        };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(DropDownAppsSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && Apps.SequenceEqual(other.Apps)
        && Display == other.Display
        && Width == other.Width
        && Height == other.Height
        && Slide == other.Slide
        && HideWhenInactive == other.HideWhenInactive
        && KeepOnTop == other.KeepOnTop;

    public override int GetHashCode() => HashCode.Combine(Enabled, Apps.Count, Display, Width, Height, Slide, HideWhenInactive, KeepOnTop);

    private static DropDownApp Checked(DropDownApp app)
    {
        var listed = app.App.Checked();
        return app with { AppId = listed.AppId, Program = listed.Program };
    }
}

/// <summary>The display a window drops down on. The names are stored in the settings file, so never rename one.</summary>
public enum DropDownDisplay
{
    /// <summary>The display with the pointer when the shortcut is pressed.</summary>
    WithPointer,

    /// <summary>The main display, where the taskbar's notification area is.</summary>
    Main,
}

/// <summary>
/// When a shown drop-down window hides because another app came to the front. The names are
/// stored in the settings file, so never rename one.
/// </summary>
public enum HideWhenInactive
{
    /// <summary>When another app comes to the front on any display.</summary>
    AnyDisplay,

    /// <summary>When another app comes to the front mostly on the drop-down window's display.</summary>
    SameDisplay,

    Never,
}
