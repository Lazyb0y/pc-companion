using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Reads the pointer, buttons, keys, full-screen state and windows from Windows.
/// </summary>
internal sealed class WindowsHotCornerEnvironment : IHotCornerEnvironment
{
    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public Rectangle? PointerClip => InputState.PointerClip;

    public PointerVisibility PointerVisibility => InputState.PointerVisibility;

    public FullScreenMode FullScreenMode => Foreground.FullScreenMode;

    public Rectangle? FrontWindowDisplay => Displays.AreaOf(Foreground.Window);

    public bool IsKeyHeld(HotCornerKey key) => InputState.IsModifierHeld(key switch
    {
        HotCornerKey.Control => ModifierKey.Control,
        HotCornerKey.Alt => ModifierKey.Alt,
        HotCornerKey.Shift => ModifierKey.Shift,
        HotCornerKey.Windows => ModifierKey.Windows,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    });

    public bool? FindWindowFrontToBack(Func<WindowPlace, bool?> decide) => TopLevelWindows.FindFrontToBack(decide);
}
