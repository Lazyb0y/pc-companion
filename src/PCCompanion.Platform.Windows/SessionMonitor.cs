using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reports when the user's Windows session is locked or disconnected, as when another user signs
/// in, and when it's unlocked or connected again, until disposed.
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    private readonly MessageWindow _window;
    private bool _disposed;

    /// <param name="window">The window that receives the session messages, on the thread that uses the monitor.</param>
    /// <exception cref="Win32Exception">Windows refused to report session changes.</exception>
    public SessionMonitor(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!PInvoke.WTSRegisterSessionNotification((HWND)window.Handle, PInvoke.NOTIFY_FOR_THIS_SESSION))
        {
            throw new Win32Exception();
        }

        _window = window;
        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>Raised on the window's thread when the session is locked or disconnected.</summary>
    public event EventHandler? Locked;

    /// <summary>Raised on the window's thread when the session is unlocked or connected again.</summary>
    public event EventHandler? Unlocked;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        PInvoke.WTSUnRegisterSessionNotification((HWND)_window.Handle);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message != PInvoke.WM_WTSSESSION_CHANGE)
        {
            return;
        }

        if (e.WParam is PInvoke.WTS_SESSION_LOCK or PInvoke.WTS_CONSOLE_DISCONNECT or PInvoke.WTS_REMOTE_DISCONNECT)
        {
            Locked?.Invoke(this, EventArgs.Empty);
        }
        else if (e.WParam is PInvoke.WTS_SESSION_UNLOCK or PInvoke.WTS_CONSOLE_CONNECT or PInvoke.WTS_REMOTE_CONNECT)
        {
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
    }
}
