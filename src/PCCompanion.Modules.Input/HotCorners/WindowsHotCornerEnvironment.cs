using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Reads the pointer, buttons, keys and full-screen state from Windows.
/// </summary>
internal sealed class WindowsHotCornerEnvironment : IHotCornerEnvironment
{
    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public bool IsFullScreenAppInFront => Foreground.IsFullScreen();

    public bool IsKeyHeld(HotCornerKey key) => InputState.IsModifierHeld(key switch
    {
        HotCornerKey.Control => ModifierKey.Control,
        HotCornerKey.Alt => ModifierKey.Alt,
        HotCornerKey.Shift => ModifierKey.Shift,
        HotCornerKey.Windows => ModifierKey.Windows,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    });
}
