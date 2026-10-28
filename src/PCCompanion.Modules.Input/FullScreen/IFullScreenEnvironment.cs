using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.FullScreen;

/// <summary>
/// What <see cref="FullScreenCheck"/> reads from Windows, so tests can supply it instead.
/// </summary>
public interface IFullScreenEnvironment
{
    FullScreenMode FullScreenMode { get; }

    /// <summary>
    /// The area of the display the window in front is on, or <see langword="null"/> when no
    /// window is in front.
    /// </summary>
    Rectangle? FrontWindowDisplay { get; }

    /// <summary>
    /// Reads the visible top-level windows front to back until <paramref name="decide"/> returns a
    /// value, and returns that value; <see langword="null"/> when no window decided.
    /// </summary>
    bool? FindWindowFrontToBack(Func<WindowPlace, bool?> decide);
}
