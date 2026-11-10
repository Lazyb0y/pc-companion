using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>What keeps a picture or folder from being used.</summary>
public enum SourceProblem
{
    None,

    /// <summary>Neither a picture nor a folder.</summary>
    NoPicture,

    /// <summary>Both a picture and a folder.</summary>
    PictureAndFolder,

    /// <summary>Characters a path can't hold, or a path that reaches a device.</summary>
    InvalidPath,

    /// <summary>Not a full path on a drive letter, such as <c>C:\Pictures\Day.jpg</c>.</summary>
    NotFullPath,

    /// <summary>A path on a network location, which Windows would wait for the network to read.</summary>
    NetworkPath,

    /// <summary>A picture in a format that not every supported Windows shows as a background.</summary>
    UnsupportedFormat,
}

/// <summary>What keeps a time of Times of day from being used.</summary>
public enum TimeProblem
{
    None,

    /// <summary>Not a time from <c>00:00</c> to <c>23:59</c>.</summary>
    InvalidTime,

    /// <summary>An earlier time in the list is the same.</summary>
    SameTime,
}

/// <summary>
/// Checks Dynamic Wallpaper's settings. A hand-edited file can hold anything, so every value is
/// checked before it's used. See "Settings" and "Security and privacy" in
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static partial class DynamicWallpaperCheck
{
    /// <summary>The fewest times Times of day has.</summary>
    public const int MinimumTimes = 2;

    /// <summary>The most times Times of day has.</summary>
    public const int MaximumTimes = 12;

    /// <summary>The most displays with their own pictures.</summary>
    public const int MaximumDisplays = 16;

    // The longest path Windows takes, with long paths on.
    private const int MaximumPathLength = 32767;

    // A display's device path and name are much shorter; these only bound a hand-edited file.
    private const int MaximumConnectionLength = 512;
    private const int MaximumDisplayNameLength = 64;

    // Paths that reach Windows' devices directly, past the checks that Windows makes of a path.
    private static readonly string[] DevicePrefixes = [@"\\?\", @"\\.\", @"\??\"];

    // What can't be in a path, besides what Windows doesn't allow.
    private static readonly char[] InvalidCharacters = [.. Path.GetInvalidPathChars(), '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// The pictures' extensions: JPEG, PNG, BMP and TIFF, which Windows shows as a background on
    /// every supported version.
    /// </summary>
    public static IReadOnlyList<string> PictureExtensions { get; } = [".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".dib", ".tif", ".tiff"];

    /// <summary>Whether a file's extension is one of <see cref="PictureExtensions"/>.</summary>
    public static bool IsPicture(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return PictureExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What keeps a picture or folder from being used; <see cref="SourceProblem.None"/> when nothing does.</summary>
    public static SourceProblem Check(WallpaperSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var hasPicture = !string.IsNullOrWhiteSpace(source.Picture);
        var hasFolder = !string.IsNullOrWhiteSpace(source.Folder);
        if (hasPicture && hasFolder)
        {
            return SourceProblem.PictureAndFolder;
        }

        if (!hasPicture && !hasFolder)
        {
            return SourceProblem.NoPicture;
        }

        var path = hasPicture ? source.Picture! : source.Folder!;
        var problem = CheckPath(path);
        return problem == SourceProblem.None && hasPicture && !IsPicture(path) ? SourceProblem.UnsupportedFormat : problem;
    }

    /// <summary>What keeps a path from being used: it must be a full path on a drive letter, with no device or network path.</summary>
    public static SourceProblem CheckPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length > MaximumPathLength
            || path.IndexOfAny(InvalidCharacters) >= 0
            || DevicePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return SourceProblem.InvalidPath;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return SourceProblem.NetworkPath;
        }

        if (!DriveLetterPath().IsMatch(path) || !Path.IsPathFullyQualified(path))
        {
            return SourceProblem.NotFullPath;
        }

        // A colon after the drive letter's would name a stream of a file.
        return path.IndexOf(':', 2) >= 0 ? SourceProblem.InvalidPath : SourceProblem.None;
    }

    /// <summary>A time of Times of day, such as <c>07:00</c>.</summary>
    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>A time as Times of day writes it, such as <c>07:00</c>.</summary>
    public static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>What keeps each time from being used, in the list's order.</summary>
    public static IReadOnlyList<TimeProblem> CheckTimes(IReadOnlyList<WallpaperPeriod> times)
    {
        ArgumentNullException.ThrowIfNull(times);

        var seen = new HashSet<TimeOnly>();
        return [.. times.Select(period => !TryParseTime(period.At, out var time)
            ? TimeProblem.InvalidTime
            : seen.Add(time) ? TimeProblem.None : TimeProblem.SameTime)];
    }

    /// <summary>Whether coordinates are a place: a latitude from -90 to 90 and a longitude from -180 to 180.</summary>
    public static bool AreCoordinates(double? latitude, double? longitude) =>
        latitude is { } north && longitude is { } east
        && double.IsFinite(north) && double.IsFinite(east)
        && Math.Abs(north) <= 90 && Math.Abs(east) <= 180;

    /// <summary>
    /// Coordinates with one decimal, about 11 km, which moves sunrise and sunset by less than a
    /// minute, so no more is kept.
    /// </summary>
    public static Coordinates Round(Coordinates place) =>
        new(Math.Round(place.Latitude, 1, MidpointRounding.AwayFromZero), Math.Round(place.Longitude, 1, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Whether a display's values can recognize and name it: a model as in Windows' hardware IDs or
    /// none, a serial number hash or none, a connection or a model, and no control characters.
    /// </summary>
    public static bool IsValid(WallpaperDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);

        return (display.Model.Length == 0 || Model().IsMatch(display.Model))
            && (display.Serial.Length == 0 || Serial().IsMatch(display.Serial))
            && display.Connection.Length <= MaximumConnectionLength
            && display.Name.Length <= MaximumDisplayNameLength
            && !display.Connection.Any(char.IsControl)
            && !display.Name.Any(char.IsControl)
            && (display.Model.Length > 0 || display.Connection.Length > 0);
    }

    [GeneratedRegex(@"^[A-Za-z]:\\")]
    private static partial Regex DriveLetterPath();

    [GeneratedRegex("^[A-Z]{3}[0-9A-F]{4}$")]
    private static partial Regex Model();

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex Serial();
}
