using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.FullScreen;

/// <summary>
/// Reads what <see cref="FullScreenCheck"/> needs from Windows: its full-screen state, the window
/// in front's display and the windows from front to back.
/// </summary>
internal sealed class WindowsFullScreenEnvironment : IFullScreenEnvironment
{
    public FullScreenMode FullScreenMode => Foreground.FullScreenMode;

    public Rectangle? FrontWindowDisplay => Displays.AreaOf(Foreground.Window);

    public bool? FindWindowFrontToBack(Func<WindowPlace, bool?> decide) => TopLevelWindows.FindFrontToBack(decide);
}
