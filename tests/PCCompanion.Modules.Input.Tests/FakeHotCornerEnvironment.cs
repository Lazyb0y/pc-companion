using System.Drawing;
using PCCompanion.Modules.Input.HotCorners;

namespace PCCompanion.Modules.Input.Tests;

/// <summary>
/// The pointer, buttons, keys and full-screen state, as a test sets them.
/// </summary>
internal sealed class FakeHotCornerEnvironment : IHotCornerEnvironment
{
    public Point? PointerPosition { get; set; }

    public bool IsMouseButtonHeld { get; set; }

    public bool IsFullScreenAppInFront { get; set; }

    public HotCornerKey HeldKey { get; set; }

    public bool IsKeyHeld(HotCornerKey key) => key == HeldKey;
}
