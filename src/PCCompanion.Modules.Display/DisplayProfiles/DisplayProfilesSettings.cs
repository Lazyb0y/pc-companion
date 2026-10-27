using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>A refresh rate as the exact fraction Windows reported, such as 60000/1001 for 59.94 Hz.</summary>
public sealed record ProfileRefreshRate
{
    public uint Numerator { get; set; }

    public uint Denominator { get; set; }

    /// <summary>The rate in hertz; 0 without a denominator.</summary>
    public double Hertz() => Denominator == 0 ? 0 : (double)Numerator / Denominator;
}

/// <summary>
/// A display in a profile: what recognizes and names it, whether it's on, and for a display
/// that's on, its place on the desktop and its mode. Only the first part is written for a display
/// that's off. See "Profiles" and "Recognizing displays" in docs/features/display/display-profiles.md.
/// </summary>
public sealed record ProfileDisplay
{
    /// <summary>
    /// The maker's code and the product code from the display's EDID, such as <c>DELA0A3</c>;
    /// empty for a display without an EDID.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>A hash of the display's serial number, 16 hexadecimal digits; empty when it has none.</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>The display's device path when the profile was made, which names its connection.</summary>
    public string Connection { get; set; } = string.Empty;

    /// <summary>Whether it's a laptop's or tablet's own screen.</summary>
    public bool BuiltIn { get; set; }

    /// <summary>The display's name from its EDID, such as <c>DELL U2720Q</c>; empty when it has none.</summary>
    public string Name { get; set; } = string.Empty;

    public bool On { get; set; }

    /// <summary>Whether it's the main display.</summary>
    public bool Main { get; set; }

    /// <summary>The left edge of its part of the desktop, in pixels.</summary>
    public int X { get; set; }

    /// <summary>The top edge of its part of the desktop, in pixels.</summary>
    public int Y { get; set; }

    /// <summary>Its display resolution's width, in the display's landscape orientation.</summary>
    public int Width { get; set; }

    /// <summary>Its display resolution's height, in the display's landscape orientation.</summary>
    public int Height { get; set; }

    public ProfileRefreshRate RefreshRate { get; set; } = new();

    public DisplayOrientation Orientation { get; set; }

    /// <summary>Use HDR, on or off; <see langword="null"/> for a display without HDR.</summary>
    public bool? Hdr { get; set; }

    /// <summary>
    /// The part of the desktop it shows: its resolution, turned by its orientation, at its position.
    /// </summary>
    public Rectangle Area() => Orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped
        ? new Rectangle(X, Y, Height, Width)
        : new Rectangle(X, Y, Width, Height);
}

/// <summary>A named display setup that the user switches to.</summary>
public sealed record DisplayProfile
{
    /// <summary>The profile's name, shown in the menu.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The displays that are on, and the ones that were connected and off when it was made.</summary>
    public IReadOnlyList<ProfileDisplay> Displays { get; set; } = [];

    // Compares the displays one by one, not the lists, so the same profile read twice is equal.
    public bool Equals(DisplayProfile? other) =>
        other is not null && Name == other.Name && Displays.SequenceEqual(other.Displays);

    public override int GetHashCode() => HashCode.Combine(Name, Displays.Count);
}

/// <summary>
/// Display Profiles settings, stored in <c>features.displayProfiles</c>. See docs/features/display/display-profiles.md.
/// </summary>
public sealed record DisplayProfilesSettings : IFeatureSettings<DisplayProfilesSettings>
{
    /// <summary>The most profiles the menu shows; later ones are left out.</summary>
    public const int MaximumProfiles = 20;

    public bool Enabled { get; set; }

    public IReadOnlyList<DisplayProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static DisplayProfilesSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, DisplayProfilesFormat.Settings, () => new DisplayProfilesSettings());

    public JsonElement Write() => FeatureSettings.Write(this, DisplayProfilesFormat.Settings);

    public DisplayProfilesSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the profiles one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(DisplayProfilesSettings? other) =>
        other is not null && Enabled == other.Enabled && Profiles.SequenceEqual(other.Profiles);

    public override int GetHashCode() => HashCode.Combine(Enabled, Profiles.Count);
}
