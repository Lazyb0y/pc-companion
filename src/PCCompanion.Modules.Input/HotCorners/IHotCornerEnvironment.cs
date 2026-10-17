using System.Drawing;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What <see cref="CornerDetector"/> reads from Windows, so tests can supply it instead.
/// </summary>
public interface IHotCornerEnvironment
{
    /// <summary>
    /// The pointer position in physical pixels.
    /// </summary>
    Point PointerPosition { get; }

    bool IsMouseButtonHeld { get; }

    bool IsFullScreenAppInFront { get; }

    bool IsKeyHeld(HotCornerKey key);
}
