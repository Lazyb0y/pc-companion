using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What <see cref="CornerDetector"/> reads from Windows, so tests can supply it instead.
/// </summary>
public interface IHotCornerEnvironment
{
    /// <summary>
    /// The pointer position in physical pixels, or <see langword="null"/> when it can't be read,
    /// for example on the lock screen.
    /// </summary>
    Point? PointerPosition { get; }

    bool IsMouseButtonHeld { get; }

    /// <summary>
    /// The area the pointer is confined to, in physical pixels: the area of all displays unless an
    /// app confines it. <see langword="null"/> when it can't be read.
    /// </summary>
    Rectangle? PointerClip { get; }

    PointerVisibility PointerVisibility { get; }

    FullScreenMode FullScreenMode { get; }

    /// <summary>
    /// The area of the display the window in front is on, or <see langword="null"/> when no
    /// window is in front.
    /// </summary>
    Rectangle? FrontWindowDisplay { get; }

    bool IsKeyHeld(HotCornerKey key);

    /// <summary>
    /// Reads the visible top-level windows front to back until <paramref name="decide"/> returns a
    /// value, and returns that value; <see langword="null"/> when no window decided.
    /// </summary>
    bool? FindWindowFrontToBack(Func<WindowPlace, bool?> decide);
}
