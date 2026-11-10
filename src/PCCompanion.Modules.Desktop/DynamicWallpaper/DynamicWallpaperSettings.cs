using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>When the pictures change.</summary>
public enum ChangeWith
{
    /// <summary>Sunrise and sunset: Night, Sunrise, Day and Sunset, by the sun's height.</summary>
    Sun,

    /// <summary>Times of day: at times on the clock.</summary>
    Times,

    /// <summary>Windows mode: Light and Dark.</summary>
    WindowsMode,
}

/// <summary>Where Sunrise and sunset's place comes from.</summary>
public enum LocationSource
{
    /// <summary>The main city of Windows' time zone.</summary>
    TimeZone,

    /// <summary>A latitude and a longitude the user typed or asked Windows for.</summary>
    Coordinates,
}

/// <summary>How Windows fits the pictures to the displays, the same on every display.</summary>
public enum WallpaperFit
{
    Fill,
    Fit,
    Stretch,
    Tile,
    Center,

    /// <summary>One picture across all the displays.</summary>
    Span,
}

/// <summary>A picture, or a folder of pictures, for one display in a period.</summary>
public sealed record WallpaperSource
{
    /// <summary>A picture's full path; <see langword="null"/> with a folder.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Picture { get; set; }

    /// <summary>A folder's full path, which shows its next picture each time; <see langword="null"/> with a picture.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Folder { get; set; }
}

/// <summary>
/// A period: its picture or folder, for every display or, with a picture for each display, for
/// Other displays, and its own for each display; a time of Times of day also has its time.
/// </summary>
public sealed record WallpaperPeriod
{
    /// <summary>For Times of day, the time it starts, such as <c>07:00</c>; otherwise <see langword="null"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? At { get; set; }

    /// <inheritdoc cref="WallpaperSource.Picture"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Picture { get; set; }

    /// <inheritdoc cref="WallpaperSource.Folder"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Folder { get; set; }

    /// <summary>
    /// With a picture for each display, an entry for each of the settings' displays, in the same
    /// order; <see langword="null"/> for one that has the period's own.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WallpaperSource?>? Displays { get; set; }

    /// <summary>The period's own picture or folder.</summary>
    [JsonIgnore]
    public WallpaperSource Own => new() { Picture = Picture, Folder = Folder };

    /// <summary>The picture or folder for one of the settings' displays: its own, or the period's.</summary>
    public WallpaperSource For(int display) =>
        Displays is { } displays && display >= 0 && display < displays.Count && displays[display] is { } own ? own : Own;

    // Compares the displays one by one, not the lists, so the same period read twice is equal.
    public bool Equals(WallpaperPeriod? other) =>
        other is not null
        && At == other.At
        && Picture == other.Picture
        && Folder == other.Folder
        && (Displays ?? []).SequenceEqual(other.Displays ?? []);

    public override int GetHashCode() => HashCode.Combine(At, Picture, Folder, Displays?.Count ?? 0);
}

/// <summary>Sunrise and sunset's periods; Sunrise and Sunset may have none.</summary>
public sealed record SunPeriods
{
    public WallpaperPeriod? Sunrise { get; set; }

    public WallpaperPeriod Day { get; set; } = new();

    public WallpaperPeriod? Sunset { get; set; }

    public WallpaperPeriod Night { get; set; } = new();

    /// <summary>The period's pictures; <see langword="null"/> for Sunrise or Sunset without any.</summary>
    public WallpaperPeriod? Of(SunPeriod period) => period switch
    {
        SunPeriod.Sunrise => Sunrise,
        SunPeriod.Day => Day,
        SunPeriod.Sunset => Sunset,
        _ => Night,
    };
}

/// <summary>Windows mode's periods.</summary>
public sealed record WindowsModePeriods
{
    public WallpaperPeriod Light { get; set; } = new();

    public WallpaperPeriod Dark { get; set; } = new();
}

/// <summary>
/// A display with its own pictures, as Display Profiles keeps one: what recognizes and names it.
/// </summary>
public sealed record WallpaperDisplay
{
    /// <summary>The maker's code and the product code from the display's EDID; empty without one.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>A hash of the display's serial number, 16 hexadecimal digits; empty when it has none.</summary>
    public string Serial { get; set; } = string.Empty;

    /// <summary>The display's device path when it was added, which names its connection.</summary>
    public string Connection { get; set; } = string.Empty;

    /// <summary>Whether it's a laptop's or tablet's own screen.</summary>
    public bool BuiltIn { get; set; }

    /// <summary>The display's name from its EDID; empty without one.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What recognizes it among the connected displays.</summary>
    public KnownDisplay ToKnown() => new(Model, Serial, Connection, BuiltIn);
}

/// <summary>
/// Dynamic Wallpaper settings, stored in <c>features.dynamicWallpaper</c>. See
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed record DynamicWallpaperSettings : IFeatureSettings<DynamicWallpaperSettings>
{
    /// <summary>The times Times of day starts with.</summary>
    public static readonly IReadOnlyList<string> FirstTimes = ["07:00", "19:00"];

    public bool Enabled { get; set; }

    /// <summary>When the pictures change; an unknown choice in the file reads as the default.</summary>
    [JsonConverter(typeof(LenientEnumConverter<ChangeWith>))]
    public ChangeWith ChangeWith { get; set; }

    [JsonConverter(typeof(LenientEnumConverter<LocationSource>))]
    public LocationSource Location { get; set; }

    /// <summary>Degrees north, with Coordinates; <see langword="null"/> otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Latitude { get; set; }

    /// <summary>Degrees east, with Coordinates; <see langword="null"/> otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Longitude { get; set; }

    [JsonConverter(typeof(LenientEnumConverter<WallpaperFit>))]
    public WallpaperFit Fit { get; set; }

    /// <summary>Whether each display has its own pictures; never with <see cref="WallpaperFit.Span"/>.</summary>
    public bool EachDisplay { get; set; }

    /// <summary>The displays with their own pictures, which each period's displays follow.</summary>
    public IReadOnlyList<WallpaperDisplay> Displays { get; set; } = [];

    public SunPeriods Sun { get; set; } = new();

    public IReadOnlyList<WallpaperPeriod> Times { get; set; } = [];

    public WindowsModePeriods WindowsMode { get; set; } = new();

    /// <summary>Whether each display gets its own pictures now: a picture for each display, and not Span.</summary>
    [JsonIgnore]
    public bool PicturesForEachDisplay => EachDisplay && Fit != WallpaperFit.Span;

    /// <summary>
    /// The settings a feature starts with: Sunrise and sunset, From your time zone, the same picture
    /// on every display, the day picture for Day, Light and 7:00, the night picture for Night, Dark
    /// and 19:00, and Windows' fit.
    /// </summary>
    public static DynamicWallpaperSettings First(string dayPicture, string nightPicture, WallpaperFit fit)
    {
        ArgumentNullException.ThrowIfNull(dayPicture);
        ArgumentNullException.ThrowIfNull(nightPicture);

        return new DynamicWallpaperSettings
        {
            Fit = fit,
            Sun = new SunPeriods { Day = new() { Picture = dayPicture }, Night = new() { Picture = nightPicture } },
            Times =
            [
                new() { At = FirstTimes[0], Picture = dayPicture },
                new() { At = FirstTimes[1], Picture = nightPicture },
            ],
            WindowsMode = new WindowsModePeriods { Light = new() { Picture = dayPicture }, Dark = new() { Picture = nightPicture } },
        };
    }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static DynamicWallpaperSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, DynamicWallpaperJsonContext.Default.DynamicWallpaperSettings, () => new DynamicWallpaperSettings());

    /// <summary>
    /// The names of the choices in the section that aren't known, such as a <c>fit</c> written by a
    /// newer version, which <see cref="Read"/> reads as their defaults.
    /// </summary>
    public static IReadOnlyList<string> UnknownChoices(JsonElement? section)
    {
        var unknown = new List<string>();
        if (section is not { ValueKind: JsonValueKind.Object } value)
        {
            return unknown;
        }

        Check<ChangeWith>("changeWith");
        Check<LocationSource>("location");
        Check<WallpaperFit>("fit");
        return unknown;

        void Check<TEnum>(string name)
            where TEnum : struct, Enum
        {
            if (value.TryGetProperty(name, out var choice) && !LenientEnumConverter<TEnum>.TryParse(choice, out _))
            {
                unknown.Add(name);
            }
        }
    }

    public JsonElement Write() => FeatureSettings.Write(this, DynamicWallpaperJsonContext.Default.DynamicWallpaperSettings);

    public DynamicWallpaperSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the lists item by item, so the same settings read twice are equal.
    public bool Equals(DynamicWallpaperSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && ChangeWith == other.ChangeWith
        && Location == other.Location
        && Latitude == other.Latitude
        && Longitude == other.Longitude
        && Fit == other.Fit
        && EachDisplay == other.EachDisplay
        && Displays.SequenceEqual(other.Displays)
        && Sun == other.Sun
        && Times.SequenceEqual(other.Times)
        && WindowsMode == other.WindowsMode;

    public override int GetHashCode() => HashCode.Combine(Enabled, ChangeWith, Location, Fit, EachDisplay, Displays.Count, Times.Count);
}
