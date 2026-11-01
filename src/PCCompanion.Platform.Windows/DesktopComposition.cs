using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The desktop's compositor, which draws every window to the screen once per frame of the display.
/// </summary>
public static class DesktopComposition
{
    /// <summary>
    /// Waits until the compositor has drawn its next frame (<c>DwmFlush</c>), at most about one
    /// frame's time, so that moves made once per frame are each drawn. Returns false when it
    /// couldn't wait, for example without composition.
    /// </summary>
    public static bool WaitForNextFrame() => PInvoke.DwmFlush().Succeeded;
}
