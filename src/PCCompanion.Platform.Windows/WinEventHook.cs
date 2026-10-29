using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Reports events that Windows raises for windows and other objects, such as a window coming to
/// the front or being shown, until disposed. It's an out-of-context hook, which runs no code in
/// other programs: Windows delivers the reports through the message loop of the thread that
/// creates the hook. Create, use and dispose it on that thread, which must run a
/// <see cref="MessageLoop"/>.
/// </summary>
public sealed class WinEventHook : IDisposable
{
    /// <summary>A window came to the front (<c>EVENT_SYSTEM_FOREGROUND</c>).</summary>
    public const uint ForegroundEvent = 0x0003;

    /// <summary>
    /// Another desktop started getting input, such as the secure desktop of a UAC prompt, or the
    /// user's own again (<c>EVENT_SYSTEM_DESKTOPSWITCH</c>).
    /// </summary>
    public const uint DesktopSwitchEvent = 0x0020;

    /// <summary>A window started to minimize (<c>EVENT_SYSTEM_MINIMIZESTART</c>).</summary>
    public const uint MinimizeStartEvent = 0x0016;

    /// <summary>An object was destroyed, also when its process ended (<c>EVENT_OBJECT_DESTROY</c>).</summary>
    public const uint ObjectDestroyEvent = 0x8001;

    /// <summary>An object was shown (<c>EVENT_OBJECT_SHOW</c>).</summary>
    public const uint ObjectShowEvent = 0x8002;

    /// <summary>An object was hidden (<c>EVENT_OBJECT_HIDE</c>).</summary>
    public const uint ObjectHideEvent = 0x8003;

    /// <summary>A window's children changed their order, as when windows come in front of others (<c>EVENT_OBJECT_REORDER</c>).</summary>
    public const uint ObjectReorderEvent = 0x8004;

    /// <summary>An object's name changed, as a window's title does (<c>EVENT_OBJECT_NAMECHANGE</c>).</summary>
    public const uint ObjectNameChangeEvent = 0x800C;

    // Kept in a field so the delegate lives as long as the hook.
    private readonly WINEVENTPROC _callback;
    private readonly UnhookWinEventSafeHandle _hook;
    private bool _disposed;

    /// <param name="firstEvent">The first of the events to report, such as <see cref="ObjectShowEvent"/>.</param>
    /// <param name="lastEvent">The last of the events to report, at least <paramref name="firstEvent"/>.</param>
    /// <param name="processId">The process whose events to report, or 0 for every process.</param>
    /// <param name="skipOwnProcess">Leaves out this process's own events.</param>
    /// <exception cref="Win32Exception">Windows refused the hook.</exception>
    public WinEventHook(uint firstEvent, uint lastEvent, uint processId = 0, bool skipOwnProcess = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lastEvent, firstEvent);

        _callback = OnEvent;
        var flags = PInvoke.WINEVENT_OUTOFCONTEXT | (skipOwnProcess ? PInvoke.WINEVENT_SKIPOWNPROCESS : 0);
        _hook = PInvoke.SetWinEventHook(firstEvent, lastEvent, null, _callback, processId, 0, flags);
        if (_hook.IsInvalid)
        {
            throw new Win32Exception();
        }
    }

    /// <summary>Raised on the hook's thread for each event.</summary>
    public event EventHandler<WinEventArgs>? Raised;

    public void Dispose()
    {
        _disposed = true;
        _hook.Dispose();
    }

    // Reports queued before the hook was removed can still arrive. Timed on a watched thread.
    private void OnEvent(HWINEVENTHOOK hook, uint @event, HWND window, int objectId, int childId, uint thread, uint time)
    {
        if (!_disposed)
        {
            var timing = SlowHandlerWatch.Begin();
            Raised?.Invoke(this, new WinEventArgs(@event, window, objectId, childId));
            SlowHandlerWatch.End(timing, SlowHandlerWatch.WindowEventSource, @event);
        }
    }
}

/// <summary>An event a <see cref="WinEventHook"/> reports.</summary>
/// <param name="eventType">Which event, such as <see cref="WinEventHook.ObjectShowEvent"/>.</param>
/// <param name="window">The window the event is about, or 0.</param>
/// <param name="objectId">The part of the window: 0 for the window itself.</param>
/// <param name="childId">The child of that part: 0 for the part itself.</param>
public sealed class WinEventArgs(uint eventType, nint window, int objectId, int childId) : EventArgs
{
    public uint EventType { get; } = eventType;

    public nint Window { get; } = window;

    public int ObjectId { get; } = objectId;

    public int ChildId { get; } = childId;

    /// <summary>Whether the event is about a window itself, rather than a part of it such as its caret.</summary>
    public bool IsAboutWindow => Window != 0 && ObjectId == 0 && ChildId == 0;
}
