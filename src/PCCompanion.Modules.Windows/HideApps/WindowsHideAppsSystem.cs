using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Hide Apps' view of Windows. Its hooks are out-of-context, so they run no code in other apps,
/// and report on the feature's thread, which creates and disposes it.
/// </summary>
public sealed class WindowsHideAppsSystem : IHideAppsSystem, IDisposable
{
    private readonly Action<Action> _guard;
    private readonly WindowsAppIdentityReader _identity = new();
    private readonly Dictionary<uint, WinEventHook> _watched = [];
    private WinEventHook? _restores;
    private WinEventHook? _order;

    /// <param name="guard">Runs a report inside the feature thread's error boundary.</param>
    public WindowsHideAppsSystem(Action<Action> guard)
    {
        ArgumentNullException.ThrowIfNull(guard);

        _guard = guard;
    }

    /// <summary>A window started to come back from minimized.</summary>
    public event EventHandler<nint>? WindowRestored;

    /// <summary>A window of a watched process was destroyed.</summary>
    public event EventHandler<nint>? WindowDestroyed;

    /// <summary>Windows changed the order of windows, while that's watched.</summary>
    public event EventHandler? OrderChanged;

    public nint ForegroundWindow => Foreground.Window;

    public string? WindowAppId(nint window) => _identity.WindowAppId(window);

    public string? PackageAppId(uint processId) => _identity.PackageAppId(processId);

    public string? ProgramPath(uint processId) => _identity.ProgramPath(processId);

    public IReadOnlyList<TopLevelWindow> ListWindows() => TopLevelWindows.List();

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window);

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool IsMinimized(nint window) => TopLevelWindows.IsMinimized(window);

    public bool IsEnabled(nint window) => TopLevelWindows.IsEnabled(window);

    public bool HasMinimizeButton(nint window) => TopLevelWindows.HasMinimizeButton(window);

    public string ClassName(nint window) => TopLevelWindows.ClassName(window);

    public Rectangle? DisplayOf(nint window) => Displays.AreaOf(window);

    public bool IsNotResponding(nint window) => TopLevelWindows.IsNotResponding(window);

    public bool Minimize(nint window) => TopLevelWindows.Minimize(window);

    public bool MinimizeWithoutActivating(nint window) => TopLevelWindows.ShowMinimized(window);

    public bool RestoreWithoutActivating(nint window) => TopLevelWindows.RestoreWithoutActivating(window);

    public bool AskToMinimize(nint window) => TopLevelWindows.AskToMinimize(window);

    public bool AskToRestore(nint window) => TopLevelWindows.AskToRestore(window);

    public bool PutBehind(nint window, nint inFront) => TopLevelWindows.PutBehind(window, inFront);

    public nint NextVisibleBehind(nint window) => TopLevelWindows.NextVisibleBehind(window);

    public bool IsAppWindow(nint window) =>
        TopLevelWindows.IsVisible(window) && !TopLevelWindows.IsCloaked(window) && TopLevelWindows.MayHaveTaskbarButton(window);

    public bool Activate(nint window) => TopLevelWindows.Activate(window);

    public void PlayWarning() => WarningSound.Play();

    public void WatchRestores(bool watch)
    {
        if (watch && _restores is null)
        {
            // Every process's windows: an app's window can come back from its taskbar button, Alt + Tab or the app.
            _restores = new WinEventHook(WinEventHook.MinimizeEndEvent, WinEventHook.MinimizeEndEvent, skipOwnProcess: true);
            _restores.Raised += (_, e) => _guard(() =>
            {
                if (e.IsAboutWindow)
                {
                    WindowRestored?.Invoke(this, e.Window);
                }
            });
        }
        else if (!watch && _restores is not null)
        {
            _restores.Dispose();
            _restores = null;
        }
    }

    public void Watch(uint processId)
    {
        if (_watched.ContainsKey(processId))
        {
            return;
        }

        var hook = new WinEventHook(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectDestroyEvent, processId);
        hook.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                WindowDestroyed?.Invoke(this, e.Window);
            }
        });
        _watched[processId] = hook;
    }

    public void Unwatch(uint processId)
    {
        if (_watched.Remove(processId, out var hook))
        {
            hook.Dispose();
        }
    }

    public void WatchOrder(bool watch)
    {
        if (watch && _order is null)
        {
            // Windows reports a change in the order of top-level windows on the desktop, not on the window that moved.
            _order = new WinEventHook(WinEventHook.ObjectReorderEvent, WinEventHook.ObjectReorderEvent, skipOwnProcess: true);
            _order.Raised += (_, _) => _guard(() => OrderChanged?.Invoke(this, EventArgs.Empty));
        }
        else if (!watch && _order is not null)
        {
            _order.Dispose();
            _order = null;
        }
    }

    public void Dispose()
    {
        WatchRestores(false);
        WatchOrder(false);
        foreach (var processId in new List<uint>(_watched.Keys))
        {
            Unwatch(processId);
        }
    }
}
