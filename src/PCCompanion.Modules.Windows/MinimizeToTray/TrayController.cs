using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// Decides which windows go to the tray and when they come back, and keeps their apps'
/// notification icons. See docs/features/windows/minimize-to-tray.md. Use it on the feature's
/// thread, except <see cref="Windows"/> and <see cref="ReleaseNow"/>.
/// </summary>
public sealed partial class TrayController : IDisposable
{
    private const string SharedAppFrameClass = "ApplicationFrameWindow";

    // Windows that start to minimize this close together were minimized at once, as by Show desktop.
    private static readonly TimeSpan Together = TimeSpan.FromMilliseconds(100);

    // How often, and how many times, a window brought back is checked for having shown, to bring it to the front.
    private static readonly TimeSpan ShownCheck = TimeSpan.FromMilliseconds(50);
    private const int ShownChecks = 20;

    private readonly ITrayWindowsSystem _system;
    private readonly ITrayIcons _icons;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly TrayApps _tray = new();
    private readonly Dictionary<string, TrayLooks> _looks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Pending> _pending = [];
    private readonly List<(nint Window, long Timestamp)> _minimizeStarts = [];
    private readonly List<ITimer> _timers = [];
    private MinimizeToTraySettings _settings = new();
    private IReadOnlyList<TrayWindowInfo> _windows = [];

    /// <param name="post">
    /// Runs an action later on the feature's thread, inside its error boundary, as timers need.
    /// </param>
    public TrayController(ITrayWindowsSystem system, ITrayIcons icons, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _icons = icons;
        _time = time;
        _post = post;
        _logger = logger;
    }

    /// <summary>
    /// The windows in the tray, the ones that went last first, for the PC Companion icon's menu.
    /// It can be read on any thread.
    /// </summary>
    public IReadOnlyList<TrayWindowInfo> Windows => Volatile.Read(ref _windows);

    /// <summary>Whether windows starting to minimize matter: for Shift + Minimize or listed apps.</summary>
    public bool WatchesMinimizes => _settings.ShiftMinimize || _settings.MinimizedApps.Count > 0;

    /// <summary>
    /// Brings back on the taskbar, minimized, every hidden window that carries the mark, as left
    /// by a PC Companion that ended without bringing them back, and removes the marks. Returns how
    /// many came back.
    /// </summary>
    public static int Recover(ITrayWindowsSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var recovered = 0;
        foreach (var window in system.FindMarked())
        {
            if (!system.IsVisible(window) && system.ShowMinimized(window))
            {
                recovered++;
            }

            system.Unmark(window);
        }

        return recovered;
    }

    public void Apply(MinimizeToTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
    }

    /// <summary>Sends the window in front, or the window that owns it, to the tray, for the shortcut.</summary>
    public SendOutcome SendFront()
    {
        var front = _system.ForegroundWindow;
        var outcome = front == 0 ? SendOutcome.NoWindow : Send(_system.RootOwner(front), SendWay.Shortcut);
        if (outcome is not (SendOutcome.Sent or SendOutcome.AlreadyInTray))
        {
            _system.PlayWarning();
        }

        return outcome;
    }

    /// <summary>A window started to minimize.</summary>
    public void OnMinimizeStart(nint window)
    {
        var now = _time.GetTimestamp();
        // A listed app's window is decided on Together after it started, against the starts Together around it.
        _minimizeStarts.RemoveAll(start => _time.GetElapsedTime(start.Timestamp, now) > Together * 2);
        _minimizeStarts.Add((window, now));

        if (_tray.AppOf(window) is not null || IsPending(window))
        {
            return;
        }

        var windowsKeyHeld = _system.IsWindowsKeyHeld;
        if (_settings.ShiftMinimize && _system.IsShiftHeld && !windowsKeyHeld)
        {
            var outcome = Send(window, SendWay.ShiftMinimize);
            if (outcome != SendOutcome.Sent)
            {
                _system.PlayWarning();
            }

            return;
        }

        if (_settings.MinimizedApps.Count == 0 || !_system.MayHaveTaskbarButton(window))
        {
            return;
        }

        var app = AppOf(window);
        if (!_settings.MinimizedApps.Any(listed => listed.Matches(app)))
        {
            return;
        }

        if (windowsKeyHeld)
        {
            LogNotSent(_logger, SendWay.ListedApp, SendOutcome.WindowsKeyHeld);
            return;
        }

        // Show desktop and the like minimize every window within a few milliseconds, so the
        // decision waits until no other window can be part of the same minimize.
        After(Together, () => DecideListed(window, now));
    }

    /// <summary>A window of a watched process was shown.</summary>
    public void OnShown(nint window)
    {
        if (_tray.AppOf(window) is not null)
        {
            Leave(window, LeaveReason.AppShowedIt);
        }
    }

    /// <summary>A window of a watched process was destroyed.</summary>
    public void OnDestroyed(nint window)
    {
        _pending.RemoveAll(pending => pending.Window == window);
        if (_tray.AppOf(window) is not null)
        {
            Leave(window, LeaveReason.Closed);
        }
    }

    /// <summary>A window of a watched process changed its title.</summary>
    public void OnTitleChanged(nint window)
    {
        if (_tray.Find(window) is not { } listed)
        {
            return;
        }

        listed.Title = _system.Title(window);
        var app = _tray.AppOf(window)!;
        UpdateIcon(app);
        RequestPicture(app, window);
        Publish();
    }

    /// <summary>Brings back the window of the app with icon <paramref name="iconId"/> that went last, as selecting the icon does.</summary>
    public bool BringBackApp(uint iconId)
    {
        if (_tray.AppWithId(iconId) is not { } app)
        {
            return false;
        }

        BringBack(app.Windows[0].Handle);
        return true;
    }

    /// <summary>Brings back one window, restored and in front.</summary>
    public void BringBack(nint window)
    {
        if (_tray.AppOf(window) is null)
        {
            return;
        }

        Leave(window, LeaveReason.BroughtBack);
        if (!_system.Exists(window))
        {
            return;
        }

        _system.Restore(window);
        BringToFrontOnceShown(window);
    }

    /// <summary>Brings back every window of the app with icon <paramref name="iconId"/>, or every window in the tray.</summary>
    public void BringAllBack(uint? iconId = null)
    {
        var windows = _tray.Apps
            .Where(app => iconId is null || app.Id == iconId)
            .SelectMany(app => app.Windows)
            .Select(window => window.Handle)
            .ToList();

        // The window that went last ends in front.
        for (var at = windows.Count - 1; at >= 0; at--)
        {
            BringBack(windows[at]);
        }
    }

    /// <summary>The menu of the app with icon <paramref name="iconId"/>, or none when it's gone.</summary>
    public IReadOnlyList<TrayMenuItem> MenuOf(uint iconId)
    {
        if (_tray.AppWithId(iconId) is not { } app)
        {
            return [];
        }

        var items = app.Windows.Select(window => new TrayMenuItem(TrayMenuCommand.BringBack, TitleOrName(window, app), window.Handle)).ToList();
        if (app.Windows.Count > 1)
        {
            items.Add(new TrayMenuItem(TrayMenuCommand.BringAllBack, Strings.MinimizeToTrayBringAllBack));
        }

        items.Add(new TrayMenuItem(TrayMenuCommand.Settings, Strings.MinimizeToTrayMenuSettings));
        return items;
    }

    /// <summary>Carries out a command chosen from the menu of the app with icon <paramref name="iconId"/>; the thread opens the settings.</summary>
    public void Choose(uint iconId, TrayMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        switch (item.Command)
        {
            case TrayMenuCommand.BringBack:
                BringBack(item.Window);
                break;
            case TrayMenuCommand.BringAllBack:
                BringAllBack(iconId);
                break;
        }
    }

    /// <summary>
    /// Brings every window in the tray, and every window on its way there, back on the taskbar,
    /// minimized and without taking the front, as PC Companion stops or the session ends. Returns
    /// how many came back.
    /// </summary>
    public int ReleaseAll()
    {
        foreach (var pending in _pending)
        {
            _system.Unmark(pending.Window);
        }

        _pending.Clear();
        var windows = _tray.Apps.SelectMany(app => app.Windows).Select(window => window.Handle).ToList();
        foreach (var window in windows)
        {
            Leave(window, LeaveReason.Released);
            _system.ShowMinimized(window);
        }

        return windows.Count;
    }

    /// <summary>
    /// Brings the windows in the tray back on the taskbar at once, from any thread, as a crash ends
    /// the host. It changes nothing else, since the process is ending.
    /// </summary>
    public void ReleaseNow()
    {
        foreach (var window in Windows)
        {
            _system.ShowMinimized(window.Window);
            _system.Unmark(window.Window);
        }
    }

    public void Dispose()
    {
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }

        _timers.Clear();
    }

    private bool IsPending(nint window) => _pending.Any(pending => pending.Window == window);

    private SendOutcome Send(nint window, SendWay way)
    {
        var outcome = Check(window);
        if (outcome != SendOutcome.Sent)
        {
            LogNotSent(_logger, way, outcome);
            return outcome;
        }

        if (!_system.Mark(window))
        {
            LogNotSent(_logger, way, SendOutcome.Refused);
            return SendOutcome.Refused;
        }

        // The shortcut's window is still on screen; the others are minimizing already.
        if (way == SendWay.Shortcut && !_system.Minimize(window))
        {
            _system.Unmark(window);
            LogNotSent(_logger, way, SendOutcome.Refused);
            return SendOutcome.Refused;
        }

        var app = AppOf(window);
        var key = KeyOf(app);
        if (_looks.TryGetValue(key, out var looks))
        {
            return Commit(window, way, key, looks, afterWaiting: false);
        }

        // The icon is added with the app's name, which Windows keeps, so the window waits, minimized, until it's read.
        var first = !_pending.Any(pending => string.Equals(pending.Key, key, StringComparison.OrdinalIgnoreCase));
        _pending.Add(new Pending(window, way, key));
        if (first)
        {
            _system.ReadLooks(app, _system.IconSize(), read => LooksRead(key, read));
        }

        return SendOutcome.Sent;
    }

    private SendOutcome Check(nint window)
    {
        if (window == 0 || !_system.Exists(window))
        {
            return SendOutcome.NoWindow;
        }

        if (_tray.AppOf(window) is not null || IsPending(window))
        {
            return SendOutcome.AlreadyInTray;
        }

        if (!_system.MayHaveTaskbarButton(window))
        {
            return SendOutcome.NotOnTaskbar;
        }

        if (string.Equals(_system.ClassName(window), SharedAppFrameClass, StringComparison.Ordinal))
        {
            return SendOutcome.SharedAppFrame;
        }

        return _system.IsEnabled(window) ? SendOutcome.Sent : SendOutcome.DialogOpen;
    }

    private void DecideListed(nint window, long started)
    {
        var together = _minimizeStarts.Any(start => start.Window != window && _time.GetElapsedTime(Math.Min(start.Timestamp, started), Math.Max(start.Timestamp, started)) <= Together);
        if (together)
        {
            LogNotSent(_logger, SendWay.ListedApp, SendOutcome.MinimizedWithOthers);
            return;
        }

        if (_system.IsMinimized(window))
        {
            _ = Send(window, SendWay.ListedApp);
        }
    }

    private void LooksRead(string key, TrayLooks looks)
    {
        _looks[key] = looks;
        var waiting = _pending.Where(pending => string.Equals(pending.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
        _pending.RemoveAll(waiting.Contains);
        foreach (var pending in waiting)
        {
            if (Commit(pending.Window, pending.Way, key, looks, afterWaiting: true) == SendOutcome.Refused && pending.Way != SendWay.ListedApp)
            {
                _system.PlayWarning();
            }
        }
    }

    private SendOutcome Commit(nint window, SendWay way, string key, TrayLooks looks, bool afterWaiting)
    {
        // While its app's name was read, the window can have closed, and one that was minimizing,
        // the user can have shown again. The shortcut's window may not be minimized yet: its
        // program may not have handled the minimize, as one that stopped responding hasn't.
        if (!_system.Exists(window) || (afterWaiting && way != SendWay.Shortcut && !_system.IsMinimized(window)))
        {
            _system.Unmark(window);
            LogNotSent(_logger, way, SendOutcome.NoWindow);
            return SendOutcome.NoWindow;
        }

        var size = _system.IconSize();
        var processId = _system.ProcessIdOf(window);
        var watched = _tray.HasProcess(processId);
        var app = _tray.Add(key, looks.Name, new TrayWindow(window, processId, _system.Title(window)));
        app.Picture = _system.ClassIcon(window, size) ?? looks.ProgramIcon ?? app.Picture ?? _system.Glyph(size);
        UpdateIcon(app);
        if (!watched)
        {
            _system.Watch(processId);
        }

        if (!_system.Hide(window))
        {
            Leave(window, LeaveReason.Released);
            LogNotSent(_logger, way, SendOutcome.Refused);
            return SendOutcome.Refused;
        }

        RequestPicture(app, window);
        Publish();
        LogSent(_logger, way, _tray.WindowCount);
        return SendOutcome.Sent;
    }

    // Takes the window out of the tray, updating or removing its app's icon.
    private void Leave(nint window, LeaveReason reason)
    {
        if (_tray.Find(window) is not { } listed)
        {
            return;
        }

        var app = _tray.Remove(window)!;

        _system.Unmark(window);
        if (app.Windows.Count == 0)
        {
            _icons.Remove(app.Id);
        }
        else
        {
            UpdateIcon(app);
        }

        // A window that was destroyed has no process any more, so its own is kept.
        if (!_tray.HasProcess(listed.ProcessId))
        {
            _system.Unwatch(listed.ProcessId);
        }

        Publish();
        LogLeft(_logger, reason, _tray.WindowCount);
    }

    private void UpdateIcon(TrayApp app)
    {
        if (app.Picture is not { } picture)
        {
            return;
        }

        var toolTip = ToolTip(app);
        if (app.Shown is { } shown && ReferenceEquals(shown.Picture, picture) && shown.ToolTip == toolTip)
        {
            return;
        }

        _ = _icons.Show(app.Id, picture, _system.IconSize(), app.Name, toolTip);
        app.Shown = (picture, toolTip);
    }

    private void RequestPicture(TrayApp app, nint window)
    {
        _ = _system.RequestIcon(window, _system.IconSize(), pixels =>
        {
            // Only the app's newest window gives its icon its picture, while it's still in the tray.
            if (pixels is not null && _tray.AppOf(window) is { } current && current.Windows[0].Handle == window)
            {
                if (current.Picture is { } old && old.AsSpan().SequenceEqual(pixels))
                {
                    return;
                }

                current.Picture = pixels;
                UpdateIcon(current);
            }
        });
    }

    private void BringToFrontOnceShown(nint window)
    {
        var checks = 0;
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || !_timers.Contains(timer))
            {
                return;
            }

            checks++;
            var shown = _system.IsVisible(window);
            if (shown)
            {
                _system.Activate(window);
            }

            if (shown || checks >= ShownChecks || !_system.Exists(window))
            {
                _timers.Remove(timer);
                timer.Dispose();
            }
        }), null, ShownCheck, ShownCheck);
        _timers.Add(timer);
    }

    private void After(TimeSpan delay, Action action)
    {
        ITimer? timer = null;
        timer = _time.CreateTimer(_ => _post(() =>
        {
            if (timer is null || !_timers.Remove(timer))
            {
                return;
            }

            timer.Dispose();
            action();
        }), null, delay, Timeout.InfiniteTimeSpan);
        _timers.Add(timer);
    }

    private void Publish() =>
        Volatile.Write(ref _windows, _tray.Apps
            .SelectMany(app => app.Windows.Select(window => new TrayWindowInfo(window.Handle, app.Name, window.Title)))
            .ToList());

    private AppOfWindow AppOf(nint window)
    {
        var processId = _system.ProcessIdOf(window);
        return new AppOfWindow(_system.WindowAppId(window), _system.PackageAppId(processId), _system.ProgramPath(processId), processId);
    }

    private static string KeyOf(AppOfWindow app) =>
        app.AppId ?? app.Program ?? "process:" + app.ProcessId.ToString(CultureInfo.InvariantCulture);

    private static string TitleOrName(TrayWindow window, TrayApp app) => window.Title.Length > 0 ? window.Title : app.Name;

    // One window: its title. Several: how many, then each title on its own line, the newest first.
    private static string ToolTip(TrayApp app) =>
        app.Windows.Count == 1
            ? TitleOrName(app.Windows[0], app)
            : string.Join('\n', app.Windows.Select(window => TitleOrName(window, app)).Prepend(PluralStrings.Format("MinimizeToTrayWindowCount", app.Windows.Count)));

    private sealed record Pending(nint Window, SendWay Way, string Key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray sent a window to the tray: {Way}; {Count} windows are in the tray.")]
    private static partial void LogSent(ILogger logger, SendWay way, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray didn't send a window to the tray: {Way}, {Reason}.")]
    private static partial void LogNotSent(ILogger logger, SendWay way, SendOutcome reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A window left the tray: {Reason}; {Count} windows are in the tray.")]
    private static partial void LogLeft(ILogger logger, LeaveReason reason, int count);
}
