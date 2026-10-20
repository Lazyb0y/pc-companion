using System;
using System.ComponentModel;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A repeating timer on a window's thread, which Windows may delay by up to its tolerance so it can
/// serve it with the same wakeup as other timers (<c>SetCoalescableTimer</c>). Create, use and
/// dispose it on the window's thread, which must run a <see cref="MessageLoop"/>.
/// </summary>
public sealed class CoalescableTimer : IDisposable
{
    private static int s_lastId;

    private readonly MessageWindow _window;
    private readonly nuint _id;
    private bool _disposed;

    /// <exception cref="Win32Exception">Windows refused the timer.</exception>
    public CoalescableTimer(MessageWindow window, TimeSpan interval, TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

        _window = window;
        _id = (nuint)Interlocked.Increment(ref s_lastId);
        _window.MessageReceived += OnMessageReceived;
        if (PInvoke.SetCoalescableTimer((HWND)window.Handle, _id, (uint)interval.TotalMilliseconds, null, (uint)tolerance.TotalMilliseconds) == 0)
        {
            var exception = new Win32Exception();
            _window.MessageReceived -= OnMessageReceived;
            throw exception;
        }
    }

    /// <summary>Raised on the window's thread at each interval.</summary>
    public event EventHandler? Elapsed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        PInvoke.KillTimer((HWND)_window.Handle, _id);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == PInvoke.WM_TIMER && e.WParam == _id)
        {
            e.Handled = true;
            Elapsed?.Invoke(this, EventArgs.Empty);
        }
    }
}
