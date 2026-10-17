using System.Drawing;

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

    bool IsFullScreenAppInFront { get; }

    bool IsKeyHeld(HotCornerKey key);
}
