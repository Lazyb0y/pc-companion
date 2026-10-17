using System;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The pointer position and which buttons and keys are held now.
/// </summary>
public static class InputState
{
    /// <summary>
    /// The pointer position, in physical pixels when the process is per-monitor DPI aware, or
    /// <see langword="null"/> when it can't be read, for example on the lock screen.
    /// </summary>
    public static Point? PointerPosition => PInvoke.GetCursorPos(out var position) ? new Point(position.X, position.Y) : null;

    public static bool IsMouseButtonHeld =>
        IsDown(VIRTUAL_KEY.VK_LBUTTON) || IsDown(VIRTUAL_KEY.VK_RBUTTON) || IsDown(VIRTUAL_KEY.VK_MBUTTON)
        || IsDown(VIRTUAL_KEY.VK_XBUTTON1) || IsDown(VIRTUAL_KEY.VK_XBUTTON2);

    public static bool IsModifierHeld(ModifierKey key) => key switch
    {
        ModifierKey.Control => IsDown(VIRTUAL_KEY.VK_CONTROL),
        ModifierKey.Alt => IsDown(VIRTUAL_KEY.VK_MENU),
        ModifierKey.Shift => IsDown(VIRTUAL_KEY.VK_SHIFT),
        ModifierKey.Windows => IsDown(VIRTUAL_KEY.VK_LWIN) || IsDown(VIRTUAL_KEY.VK_RWIN),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    internal static bool IsDown(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;
}

public enum ModifierKey
{
    Control,
    Alt,
    Shift,
    Windows,
}
