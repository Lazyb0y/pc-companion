using System;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A keyboard shortcut registered with Windows for the user's session: Windows takes the key
/// press from every app and reports it to this process instead, until disposed. Windows matches
/// it by virtual key, which depends on the keyboard layout of the window in front; see
/// <see cref="KeyboardLayout"/>. Create, use and dispose it on the window's thread.
/// </summary>
public sealed class HotKey : IDisposable
{
    // IDs from 0xC000 are for shared DLLs.
    private const uint IdCount = 0xBFFF;
    private static int s_lastId;

    private readonly MessageWindow _window;
    private bool _disposed;

    private HotKey(MessageWindow window, int id)
    {
        _window = window;
        Id = id;
        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Raised on the window's thread each time the shortcut is pressed. Holding the keys down
    /// doesn't raise it again.
    /// </summary>
    public event EventHandler? Pressed;

    /// <summary>The ID Windows reports the shortcut with, in <c>WM_HOTKEY</c>.</summary>
    public int Id { get; }

    /// <summary>
    /// Registers <paramref name="virtualKey"/> pressed with <paramref name="modifiers"/>. Returns
    /// <see langword="null"/> when Windows refuses, for example because another program already
    /// has the shortcut.
    /// </summary>
    /// <param name="window">The window that receives the shortcut, on the thread that uses it.</param>
    public static HotKey? Register(MessageWindow window, HotKeyModifiers modifiers, uint virtualKey)
    {
        ArgumentNullException.ThrowIfNull(window);

        var id = (int)((uint)Interlocked.Increment(ref s_lastId) % IdCount) + 1;
        return PInvoke.RegisterHotKey((HWND)window.Handle, id, (HOT_KEY_MODIFIERS)modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, virtualKey)
            ? new HotKey(window, id)
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        PInvoke.UnregisterHotKey((HWND)_window.Handle, Id);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == PInvoke.WM_HOTKEY && e.WParam == (nuint)Id)
        {
            e.Handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>The keys held with a <see cref="HotKey"/>'s key.</summary>
[Flags]
public enum HotKeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}
