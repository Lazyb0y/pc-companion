using System;
using System.Text.Json.Serialization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// A listed app, as the taskbar groups apps, and the shortcut that drops it down. It has an app ID
/// or a program, not both, like a <see cref="ListedApp"/>.
/// </summary>
public sealed record DropDownApp
{
    /// <summary>The name the page and recent results show, as it was when the app was added.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The app ID (AppUserModelID) that the app's windows or its Store package have.</summary>
    public string? AppId { get; set; }

    /// <summary>The full path of the app's program file, for an app without an app ID.</summary>
    public string? Program { get; set; }

    /// <summary>
    /// The app's shortcut. A number in the file reads as any value, named or not; an unknown one
    /// registers no shortcut.
    /// </summary>
    public DropDownShortcut Shortcut { get; set; } = DropDownShortcut.None;

    /// <summary>The app, without its shortcut.</summary>
    [JsonIgnore]
    public ListedApp App => new() { Name = Name, AppId = AppId, Program = Program };

    /// <summary><paramref name="app"/>, dropping down with <paramref name="shortcut"/>.</summary>
    public static DropDownApp For(ListedApp app, DropDownShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new DropDownApp { Name = app.Name, AppId = app.AppId, Program = app.Program, Shortcut = shortcut };
    }
}
