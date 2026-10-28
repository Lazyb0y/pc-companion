using System;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The pointer position, which buttons and keys are held now, and which lock keys are on.
/// </summary>
public static class InputState
{
    /// <summary>
    /// The pointer position, in physical pixels when the process is per-monitor DPI aware, or
    /// <see langword="null"/> when it can't be read, for example on the lock screen.
    /// </summary>
    public static Point? PointerPosition => PInvoke.GetCursorPos(out var position) ? new Point(position.X, position.Y) : null;

    /// <summary>
    /// The area the pointer is confined to, in physical pixels when the process is per-monitor DPI
    /// aware: the area of all displays unless an app confines it, as games and virtual machines
    /// do. <see langword="null"/> when it can't be read.
    /// </summary>
    public static Rectangle? PointerClip =>
        PInvoke.GetClipCursor(out var area) ? Rectangle.FromLTRB(area.left, area.top, area.right, area.bottom) : null;

    /// <summary>
    /// Whether the pointer is shown, hidden by an app, or hidden by Windows during touch or pen
    /// input. It counts as shown when it can't be read.
    /// </summary>
    public static unsafe PointerVisibility PointerVisibility
    {
        get
        {
            var info = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
            return !PInvoke.GetCursorInfo(ref info) ? PointerVisibility.Shown
                : (info.flags & CURSORINFO_FLAGS.CURSOR_SUPPRESSED) != 0 ? PointerVisibility.Suppressed
                : (info.flags & CURSORINFO_FLAGS.CURSOR_SHOWING) != 0 ? PointerVisibility.Shown
                : PointerVisibility.Hidden;
        }
    }

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

    /// <summary>
    /// Whether a lock key is on. Microsoft documents this as the calling thread's copy of the
    /// keyboard state, which follows the keyboard messages the thread reads. Windows also brings
    /// the copy up to date while the thread has no keyboard input waiting, so a thread that never
    /// has the keyboard focus, such as the host's UI thread, reads the current state, also while
    /// an app running as administrator is in front. See "Detecting a change" in
    /// docs/features/input/lock-keys.md.
    /// </summary>
    public static bool IsLockKeyOn(LockKey key) => (PInvoke.GetKeyState((int)(key switch
    {
        LockKey.CapsLock => VIRTUAL_KEY.VK_CAPITAL,
        LockKey.NumLock => VIRTUAL_KEY.VK_NUMLOCK,
        LockKey.ScrollLock => VIRTUAL_KEY.VK_SCROLL,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    })) & 1) != 0;

    internal static bool IsDown(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;
}

public enum PointerVisibility
{
    Shown,

    /// <summary>
    /// An app hid the pointer, as a game does.
    /// </summary>
    Hidden,

    /// <summary>
    /// Windows hides the pointer during touch or pen input.
    /// </summary>
    Suppressed,
}

public enum ModifierKey
{
    Control,
    Alt,
    Shift,
    Windows,
}

public enum LockKey
{
    CapsLock,
    NumLock,
    ScrollLock,
}
