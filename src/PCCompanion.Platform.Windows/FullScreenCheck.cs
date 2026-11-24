using System.Drawing;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Whether an app is full screen on a display, for the features that stay out of its way. See
/// "Full screen and a captured pointer" in docs/features/input/hot-corners.md.
/// </summary>
public static class FullScreenCheck
{
    public static bool IsFullScreen(IFullScreenEnvironment environment, Rectangle display) => environment.FullScreenMode switch
    {
        FullScreenMode.Presentation => true,

        // An app in exclusive full screen is the app in front.
        FullScreenMode.Exclusive when environment.FrontWindowDisplay == display => true,
        _ => environment.FindWindowFrontToBack(window => Decide(window, display)) ?? false,
    };

    /// <summary>
    /// Whether <paramref name="window"/> itself is full screen: it covers the display it's on
    /// without a frame, as a browser after F11 does.
    /// </summary>
    public static bool Covers(nint window) =>
        TopLevelWindows.PlaceOf(window) is { } place && Displays.AreaOf(window) is { } display && Decide(place, display) == true;

    // Null passes over the window to the next one.
    private static bool? Decide(WindowPlace window, Rectangle display)
    {
        if (window.IsCloaked || window.IsMinimized || !window.Bounds.IntersectsWith(display) || IsOverlay(window))
        {
            return null;
        }

        var covers = window.Bounds.Contains(display);
        if (window.IsTopmost && !covers)
        {
            return null;
        }

        return covers && !window.HasFrame && !window.IsShell;
    }

    private static bool IsOverlay(WindowPlace window) => window.IsClickThrough || window.IsNoActivate || window.IsToolWindow;
}
