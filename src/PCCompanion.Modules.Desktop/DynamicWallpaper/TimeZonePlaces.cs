using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>A place on Earth.</summary>
/// <param name="Latitude">Degrees north; negative for south.</param>
/// <param name="Longitude">Degrees east; negative for west.</param>
public readonly record struct Coordinates(double Latitude, double Longitude);

/// <summary>
/// The main city of each time zone, from the time zone database, which is in the public domain, for
/// Sunrise and sunset's place From your time zone. TimeZonePlaces.txt comes from
/// scripts/time-zone-places.ps1. See "Location" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static class TimeZonePlaces
{
    private const string Resource = "PCCompanion.Modules.Desktop.DynamicWallpaper.TimeZonePlaces.txt";

    // Each zone's coordinates in ISO 6709 form, such as +2343+09025, or "=" and the zone an older
    // name stands for.
    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    /// <summary>
    /// The main city of a time zone, in a country or region, such as Amsterdam for the time zone of
    /// Western Europe in the Netherlands; <see langword="null"/> for a time zone with no place, such as UTC.
    /// </summary>
    /// <param name="zone">The time zone, usually Windows' own.</param>
    /// <param name="region">The two-letter code of the country or region, which chooses among the time zone database's zones for a Windows time zone; <see langword="null"/> for none.</param>
    public static Coordinates? Of(TimeZoneInfo zone, string? region)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (zone.HasIanaId)
        {
            return OfZone(zone.Id);
        }

        var country = region is { Length: 2 } ? region.ToUpperInvariant() : null;
        if (country is not null && TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, country, out var inCountry) && OfZone(inCountry) is { } place)
        {
            return place;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var name) ? OfZone(name) : null;
    }

    /// <summary>
    /// The main city of a time zone of the time zone database, by its name or an older name, such as
    /// <c>Asia/Calcutta</c>; <see langword="null"/> for a name the table doesn't have.
    /// </summary>
    public static Coordinates? OfZone(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!Table.Value.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value.StartsWith('=') && !Table.Value.TryGetValue(value[1..], out value))
        {
            return null;
        }

        return Parse(value);
    }

    /// <summary>
    /// Coordinates in the time zone database's form of ISO 6709: ±DDMM±DDDMM or ±DDMMSS±DDDMMSS;
    /// <see langword="null"/> for anything else.
    /// </summary>
    public static Coordinates? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var split = text.Length < 2 ? -1 : text.IndexOfAny(['+', '-'], 1);
        if (split < 0
            || Angle(text[..split], 2) is not { } latitude
            || Angle(text[split..], 3) is not { } longitude
            || Math.Abs(latitude) > 90
            || Math.Abs(longitude) > 180)
        {
            return null;
        }

        return new Coordinates(latitude, longitude);
    }

    // A sign, the degrees, the minutes and maybe the seconds, such as +2343 or -1342511.
    private static double? Angle(string text, int degreeDigits)
    {
        var digits = text.Length - 1;
        if ((digits != degreeDigits + 2 && digits != degreeDigits + 4) || text[0] is not ('+' or '-'))
        {
            return null;
        }

        int Part(int start, int length) =>
            int.TryParse(text.AsSpan(1 + start, length), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;

        var degrees = Part(0, degreeDigits);
        var minutes = Part(degreeDigits, 2);
        var seconds = digits == degreeDigits + 4 ? Part(degreeDigits + 2, 2) : 0;
        if (degrees < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
        {
            return null;
        }

        var angle = degrees + (minutes / 60.0) + (seconds / 3600.0);
        return text[0] == '-' ? -angle : angle;
    }

    private static Dictionary<string, string> Load()
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(TimeZonePlaces).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The resource {Resource} is missing.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 0 && !line.StartsWith('#'))
            {
                table[line[..tab]] = line[(tab + 1)..];
            }
        }

        return table;
    }
}
