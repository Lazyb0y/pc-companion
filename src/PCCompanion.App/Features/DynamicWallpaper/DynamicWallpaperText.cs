using System;
using System.Globalization;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.DynamicWallpaper;

namespace PCCompanion.App.Features.DynamicWallpaper;

/// <summary>Dynamic Wallpaper's page texts for periods and problems.</summary>
public static class DynamicWallpaperText
{
    private static readonly CompositeFormat PeriodTimeFormat = CompositeFormat.Parse(Strings.DynamicWallpaperPeriodTime);

    /// <summary>A period's name, such as Day; a time of Times of day reads "From 7:00".</summary>
    public static string NameOf(PeriodName period, TimeOnly? time = null) => period.Kind switch
    {
        ChangeWith.Sun => (SunPeriod)period.Index switch
        {
            SunPeriod.Sunrise => Strings.DynamicWallpaperPeriodSunrise,
            SunPeriod.Day => Strings.DynamicWallpaperPeriodDay,
            SunPeriod.Sunset => Strings.DynamicWallpaperPeriodSunset,
            _ => Strings.DynamicWallpaperPeriodNight,
        },
        ChangeWith.WindowsMode => period.Index == 0 ? Strings.DynamicWallpaperPeriodLight : Strings.DynamicWallpaperPeriodDark,
        _ => string.Format(CultureInfo.CurrentCulture, PeriodTimeFormat, time is { } at ? FormatTime(at) : "?"),
    };

    /// <summary>A time in the user's regional format, 12-hour or 24-hour, such as 7:00 PM.</summary>
    public static string FormatTime(TimeOnly time) => time.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>Why the settings' picture or folder can't be used; <see langword="null"/> when it can.</summary>
    public static string? Of(SourceProblem problem) => problem switch
    {
        SourceProblem.None => null,
        SourceProblem.NoPicture => Strings.DynamicWallpaperProblemNoPicture,
        SourceProblem.NetworkPath => Strings.DynamicWallpaperProblemOnNetwork,
        SourceProblem.UnsupportedFormat => Strings.DynamicWallpaperProblemFormat,
        _ => Strings.DynamicWallpaperProblemPath,
    };

    /// <summary>Why a picture or folder can't be used now; <see langword="null"/> when it can.</summary>
    public static string? Of(PictureState state) => state switch
    {
        PictureState.Missing => Strings.DynamicWallpaperProblemMissing,
        PictureState.OnNetwork => Strings.DynamicWallpaperProblemOnNetwork,
        PictureState.OnlineOnly => Strings.DynamicWallpaperProblemOnlineOnly,
        _ => null,
    };

    /// <summary>Why the host can't use a picture or folder of the period in use.</summary>
    public static string Of(PictureProblem problem) => problem switch
    {
        PictureProblem.Missing => Strings.DynamicWallpaperProblemMissing,
        PictureProblem.OnNetwork => Strings.DynamicWallpaperProblemOnNetwork,
        PictureProblem.OnlineOnly => Strings.DynamicWallpaperProblemOnlineOnly,
        PictureProblem.NoPictures => Strings.DynamicWallpaperProblemNoPictures,
        PictureProblem.Refused => Strings.DynamicWallpaperProblemRefused,
        _ => Strings.DynamicWallpaperProblemPath,
    };
}
