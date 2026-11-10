using System;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>Where the sun is, seen from a place on Earth.</summary>
/// <param name="Elevation">
/// The height of the sun's center above the horizon, in degrees, without the bending of its light
/// by the air; negative below the horizon.
/// </param>
/// <param name="HourAngle">
/// How far the sun is from the place's meridian, in degrees: negative while it rises, before solar
/// noon, and positive while it sets.
/// </param>
public readonly record struct SunPosition(double Elevation, double HourAngle)
{
    /// <summary>Whether the sun is rising: between solar midnight and solar noon.</summary>
    public bool IsRising => HourAngle < 0;
}

/// <summary>
/// The sun's position, from the equations of NOAA's solar calculator, which are in the public
/// domain. They're accurate to about a minute for sunrise and sunset between 72° north and south.
/// See "Sunrise and sunset" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static class Sun
{
    // The Julian day of 1899-12-30, where OLE Automation dates start.
    private const double JulianDayOfOleDates = 2415018.5;
    private const double JulianDayOf2000 = 2451545;
    private const double DaysPerCentury = 36525;

    /// <summary>Where the sun is at a moment, seen from a place.</summary>
    /// <param name="time">The moment.</param>
    /// <param name="latitude">Degrees north; negative for south.</param>
    /// <param name="longitude">Degrees east; negative for west.</param>
    public static SunPosition At(DateTimeOffset time, double latitude, double longitude)
    {
        var utc = time.UtcDateTime;
        var century = (utc.ToOADate() + JulianDayOfOleDates - JulianDayOf2000) / DaysPerCentury;

        var meanLongitude = Modulo(280.46646 + (century * (36000.76983 + (century * 0.0003032))), 360);
        var meanAnomaly = Radians(357.52911 + (century * (35999.05029 - (0.0001537 * century))));
        var eccentricity = 0.016708634 - (century * (0.000042037 + (0.0000001267 * century)));
        var center = (Math.Sin(meanAnomaly) * (1.914602 - (century * (0.004817 + (0.000014 * century)))))
            + (Math.Sin(2 * meanAnomaly) * (0.019993 - (0.000101 * century)))
            + (Math.Sin(3 * meanAnomaly) * 0.000289);
        var node = Radians(125.04 - (1934.136 * century));
        var apparentLongitude = Radians(meanLongitude + center - 0.00569 - (0.00478 * Math.Sin(node)));
        var meanObliquity = 23 + ((26 + ((21.448 - (century * (46.815 + (century * (0.00059 - (century * 0.001813)))))) / 60)) / 60);
        var obliquity = Radians(meanObliquity + (0.00256 * Math.Cos(node)));
        var declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(apparentLongitude));

        var y = Math.Pow(Math.Tan(obliquity / 2), 2);
        var longitude0 = Radians(meanLongitude);
        var equationOfTimeMinutes = 4 * Degrees(
            (y * Math.Sin(2 * longitude0))
            - (2 * eccentricity * Math.Sin(meanAnomaly))
            + (4 * eccentricity * y * Math.Sin(meanAnomaly) * Math.Cos(2 * longitude0))
            - (0.5 * y * y * Math.Sin(4 * longitude0))
            - (1.25 * eccentricity * eccentricity * Math.Sin(2 * meanAnomaly)));

        var trueSolarMinutes = Modulo(utc.TimeOfDay.TotalMinutes + equationOfTimeMinutes + (4 * longitude), 1440);
        var hourAngle = (trueSolarMinutes / 4) - 180;

        var place = Radians(latitude);
        var cosineOfZenith = (Math.Sin(place) * Math.Sin(declination)) + (Math.Cos(place) * Math.Cos(declination) * Math.Cos(Radians(hourAngle)));
        var zenith = Degrees(Math.Acos(Math.Clamp(cosineOfZenith, -1, 1)));
        return new SunPosition(90 - zenith, hourAngle);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    private static double Modulo(double value, double divisor) => ((value % divisor) + divisor) % divisor;
}
