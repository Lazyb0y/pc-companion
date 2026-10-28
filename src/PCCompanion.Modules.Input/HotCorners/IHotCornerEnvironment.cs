using System.Drawing;
using PCCompanion.Modules.Input.FullScreen;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What <see cref="CornerDetector"/> reads from Windows, so tests can supply it instead.
/// </summary>
public interface IHotCornerEnvironment : IFullScreenEnvironment
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

    /// <summary>
    /// The app of the window in front, or <see langword="null"/> when no window is in front.
    /// </summary>
    AppOfWindow? AppInFront { get; }

    bool IsKeyHeld(HotCornerKey key);
}
