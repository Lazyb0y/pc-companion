using System;
using System.IO;
using System.Text.Json;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DynamicWallpaper;

/// <summary>
/// What Dynamic Wallpaper starts with when it's first turned on: on Windows 11, Windows' own light
/// and dark pictures, the ones of its Windows (light) and Windows (dark) themes, so it works at
/// once; on Windows 10, which has no dark picture, the current picture for both; and Windows' fit.
/// See "Pictures" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public static class DynamicWallpaperFirstSettings
{
    // Windows 11's first build.
    private const int Windows11Build = 22000;

    /// <summary>The section. It reads Windows' own setting, not Explorer's, so it's quick.</summary>
    public static JsonElement Create()
    {
        var pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper\Windows");
        var light = Path.Combine(pictures, "img0.jpg");
        var dark = Path.Combine(pictures, "img19.jpg");

        string day, night;
        if (Environment.OSVersion.Version.Build >= Windows11Build && File.Exists(light) && File.Exists(dark))
        {
            (day, night) = (light, dark);
        }
        else
        {
            day = WindowsWallpaper.SavedPicture() is { } current
                && DynamicWallpaperCheck.Check(new WallpaperSource { Picture = current }) == SourceProblem.None
                && File.Exists(current)
                ? current
                : light;
            night = day;
        }

        return DynamicWallpaperSettings.First(day, night, WallpaperFits.FitOf(WindowsWallpaper.SavedPosition())).Write();
    }
}
