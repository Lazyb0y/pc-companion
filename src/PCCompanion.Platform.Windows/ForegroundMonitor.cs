using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reports when a window of another program comes to the front, until disposed. It uses an
/// out-of-context WinEvent hook, which runs no code in other programs: Windows delivers the
/// reports through the message loop of the thread that creates the monitor. Create, use and
/// dispose it on that thread, which must run a <see cref="MessageLoop"/>.
/// </summary>
public sealed class ForegroundMonitor : IDisposable
{
    // Kept in a field so the delegate lives as long as the hook.
    private readonly WINEVENTPROC _callback;
    private readonly UnhookWinEventSafeHandle _hook;
    private bool _disposed;

    /// <exception cref="Win32Exception">Windows refused the hook.</exception>
    public ForegroundMonitor()
    {
        _callback = OnEvent;
        _hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            null,
            _callback,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);

        if (_hook.IsInvalid)
        {
            throw new Win32Exception();
        }
    }

    /// <summary>Raised on the monitor's thread when a window of another program comes to the front.</summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        _disposed = true;
        _hook.Dispose();
    }

    // Reports queued before the hook was removed can still arrive.
    private void OnEvent(HWINEVENTHOOK hook, uint @event, HWND window, int objectId, int childId, uint thread, uint time)
    {
        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
