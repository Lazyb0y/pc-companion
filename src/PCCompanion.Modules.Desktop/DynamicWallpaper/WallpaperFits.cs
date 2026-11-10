using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>The settings' fits and Windows' positions, which have the same names.</summary>
public static class WallpaperFits
{
    /// <summary>Windows' position for a fit.</summary>
    public static WallpaperPosition PositionOf(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fit => WallpaperPosition.Fit,
        WallpaperFit.Stretch => WallpaperPosition.Stretch,
        WallpaperFit.Tile => WallpaperPosition.Tile,
        WallpaperFit.Center => WallpaperPosition.Center,
        WallpaperFit.Span => WallpaperPosition.Span,
        _ => WallpaperPosition.Fill,
    };

    /// <summary>The fit for Windows' position.</summary>
    public static WallpaperFit FitOf(WallpaperPosition position) => position switch
    {
        WallpaperPosition.Fit => WallpaperFit.Fit,
        WallpaperPosition.Stretch => WallpaperFit.Stretch,
        WallpaperPosition.Tile => WallpaperFit.Tile,
        WallpaperPosition.Center => WallpaperFit.Center,
        WallpaperPosition.Span => WallpaperFit.Span,
        _ => WallpaperFit.Fill,
    };
}
