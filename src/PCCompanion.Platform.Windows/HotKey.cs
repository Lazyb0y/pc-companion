using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A keyboard shortcut registered with Windows for the user's session: Windows takes the key
/// press from every app and reports it to this process instead, until disposed. Windows matches
/// it by virtual key, which depends on the keyboard layout of the window in front; see
/// <see cref="KeyboardLayout"/>. Create, use and dispose it on the window's thread. Every
/// shortcut of this process can be paused at once, as while the settings app records a shortcut.
/// </summary>
public sealed class HotKey : IDisposable
{
    // IDs from 0xC000 are for shared DLLs.
    private const uint IdCount = 0xBFFF;
    private static readonly Lock s_gate = new();
    private static readonly HashSet<HotKey> s_registered = [];
    private static int s_lastId;

    private readonly MessageWindow _window;
    private readonly HotKeyModifiers _modifiers;
    private readonly uint _virtualKey;
    private bool _disposed;

    // Removed for a pause, on the window's thread.
    private bool _paused;

    private HotKey(MessageWindow window, int id, HotKeyModifiers modifiers, uint virtualKey)
    {
        _window = window;
        _modifiers = modifiers;
        _virtualKey = virtualKey;
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
        if (!RegisterWithWindows(window, id, modifiers, virtualKey))
        {
            return null;
        }

        var hotKey = new HotKey(window, id, modifiers, virtualKey);
        lock (s_gate)
        {
            s_registered.Add(hotKey);
        }

        return hotKey;
    }

    /// <summary>
    /// Removes every shortcut this process registered, each on its window's thread, so that
    /// Windows passes their keys to the app in front, as the settings app needs while it records a
    /// shortcut. <see cref="Resume"/> registers them again. A shortcut registered meanwhile isn't
    /// paused. Call it from any thread.
    /// </summary>
    public static void Pause() => ForEachRegistered(hotKey => hotKey.PauseOnItsThread());

    /// <summary>
    /// Registers again, each on its window's thread, the shortcuts <see cref="Pause"/> removed.
    /// One that another program took meanwhile stays off until its feature registers it again,
    /// after a change to the settings. Call it from any thread.
    /// </summary>
    public static void Resume() => ForEachRegistered(hotKey => hotKey.ResumeOnItsThread());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (s_gate)
        {
            s_registered.Remove(this);
        }

        _window.MessageReceived -= OnMessageReceived;
        if (!_paused)
        {
            PInvoke.UnregisterHotKey((HWND)_window.Handle, Id);
        }
    }

    private static bool RegisterWithWindows(MessageWindow window, int id, HotKeyModifiers modifiers, uint virtualKey) =>
        PInvoke.RegisterHotKey((HWND)window.Handle, id, (HOT_KEY_MODIFIERS)modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, virtualKey);

    private static void ForEachRegistered(Action<HotKey> action)
    {
        HotKey[] registered;
        lock (s_gate)
        {
            registered = [.. s_registered];
        }

        foreach (var hotKey in registered)
        {
            hotKey._window.BeginInvoke(() => action(hotKey));
        }
    }

    private void PauseOnItsThread()
    {
        if (!_disposed && !_paused)
        {
            _paused = PInvoke.UnregisterHotKey((HWND)_window.Handle, Id);
        }
    }

    private void ResumeOnItsThread()
    {
        if (!_disposed && _paused)
        {
            _paused = false;
            RegisterWithWindows(_window, Id, _modifiers, _virtualKey);
        }
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
