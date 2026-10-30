using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// Runs Minimize to Tray on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads"
/// in docs/features/windows/minimize-to-tray.md says: <c>Shell_NotifyIcon</c> waits for Explorer
/// with no time limit. The host's UI thread starts it, passes it new settings and choices from
/// the PC Companion icon's menu, and stops it, and never waits for it, except for
/// <see cref="MessageLoopThread{TSettings}.Join"/> as the host exits.
/// </summary>
public sealed partial class MinimizeToTrayThread : MessageLoopThread<MinimizeToTraySettings>
{
    private const string WindowClassName = "PCCompanion.MinimizeToTray";

    // WM_QUERYENDSESSION, which Windows sends before it asks apps to close at sign-out.
    private const uint QueryEndSession = 0x0011;

    // The Z key.
    private const uint KeyZ = 0x5A;

    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly HideAppsMinimized _hideApps;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Tray? _tray;

    // The feature thread's own: whether an icon's menu is open.
    private bool _menuOpen;

    public MinimizeToTrayThread(MinimizeToTraySettings settings, IHostCommands commands, TimeProvider time, ILogger logger, HideAppsMinimized hideApps, MinimizeToTrayThread? previous = null)
        : base("Minimize to Tray", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(hideApps);

        _commands = commands;
        _time = time;
        _logger = logger;
        _hideApps = hideApps;
        Start();
    }

    /// <summary>The windows in the tray, for the PC Companion icon's menu; any thread can read it.</summary>
    public IReadOnlyList<TrayWindowInfo> Windows => _tray?.Controller.Windows ?? [];

    /// <summary>Brings back a window chosen from the PC Companion icon's menu, on the feature's thread.</summary>
    public void BringBack(nint window) => Post(() => _tray?.Controller.BringBack(window));

    public void BringAllBack() => Post(() => _tray?.Controller.BringAllBack());

    /// <summary>Puts the windows in the tray back on the taskbar at once, from any thread, as the host crashes.</summary>
    public void ReleaseNow() => _tray?.Controller.ReleaseNow();

    protected override IDisposable Open(MessageWindow window)
    {
        var tray = new Tray(this, window);
        _tray = tray;
        return tray;
    }

    protected override void Apply(MinimizeToTraySettings settings) => _tray!.Apply(settings);

    // The menu would otherwise stay open, and the thread with it.
    protected override void OnStopping()
    {
        if (_menuOpen)
        {
            PopupMenu.CloseOpenMenu();
        }
    }

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception)
    {
        LogFailed(_logger, exception);

        // A failure takes the windows out of the tray, as stopping does.
        try
        {
            _tray?.Controller.ReleaseNow();
        }
        catch (Exception release)
        {
            LogReleaseFailed(_logger, release);
        }
    }

    private void ShowMenu(MessageWindow window, uint iconId, int x, int y)
    {
        var tray = _tray!;
        var items = tray.Controller.MenuOf(iconId);
        if (items.Count == 0)
        {
            return;
        }

        // A line before the settings item, which is last.
        var menu = new List<PopupMenuItem>();
        for (var at = 0; at < items.Count; at++)
        {
            if (items[at].Command == TrayMenuCommand.Settings)
            {
                menu.Add(PopupMenuItem.Separator);
            }

            menu.Add(new PopupMenuItem((uint)(at + 1), PopupMenu.Escape(items[at].Text)));
        }

        if (MenuTheme.IsSupported)
        {
            MenuTheme.Set(MenuTheme.ForWindowsMode());
        }

        uint chosen;
        _menuOpen = true;
        try
        {
            chosen = PopupMenu.Show(window, menu, x, y, Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft);
        }
        finally
        {
            _menuOpen = false;
        }

        // The menu's own loop took the request to stop, so the thread asks again and runs no command.
        if (IsStopping)
        {
            MessageLoop.Quit();
            return;
        }

        if (chosen == 0 || chosen > items.Count)
        {
            return;
        }

        var item = items[(int)chosen - 1];
        if (item.Command == TrayMenuCommand.Settings)
        {
            _commands.OpenFeatureSettings(MinimizeToTrayFeature.Definition.Id);
        }
        else
        {
            tray.Controller.Choose(iconId, item);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray's thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray applied its settings: shortcut {Shortcut}, Shift + Minimize {ShiftMinimize}, {Apps} listed apps.")]
    private static partial void LogApplied(ILogger logger, MinimizeToTrayShortcut shortcut, bool shiftMinimize, int apps);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray's shortcut was pressed: {Outcome}.")]
    private static partial void LogPressed(ILogger logger, SendOutcome outcome);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Minimize to Tray put {Count} windows back on the taskbar: {Why}.")]
    private static partial void LogReleased(ILogger logger, int count, string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Minimize to Tray's shortcut; another program may have it.")]
    private static partial void LogShortcutRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Minimize to Tray shortcut in the settings is unknown, so it has no shortcut.")]
    private static partial void LogUnknownShortcut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Minimize to Tray failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Minimize to Tray couldn't put its windows back on the taskbar after a failure.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller, its icons and hooks, and the shortcut.
    private sealed class Tray : IDisposable
    {
        private readonly MinimizeToTrayThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsTrayWindowsSystem _system;
        private readonly WindowsTrayIcons _icons;
        private HotKey? _shortcut;
        private MinimizeToTrayShortcut? _registered;
        private WinEventHook? _minimizes;

        public Tray(MinimizeToTrayThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            _system = new WindowsTrayWindowsSystem(window, owner.Guard, owner._hideApps);
            _icons = new WindowsTrayIcons(window);
            Controller = new TrayController(_system, _icons, owner._time, action => window.BeginInvoke(() => owner.Guard(action)), owner._logger);

            _system.WindowShown += (_, shown) => Controller.OnShown(shown);
            _system.WindowDestroyed += (_, destroyed) => Controller.OnDestroyed(destroyed);
            _system.TitleChanged += (_, changed) => Controller.OnTitleChanged(changed);
            _icons.Selected += (_, id) => owner.Guard(() => Controller.BringBackApp(id));
            _icons.MenuRequested += (_, e) => owner.Guard(() => owner.ShowMenu(window, e.Id, e.Point.X, e.Point.Y));
            _window.MessageReceived += OnMessageReceived;
        }

        public TrayController Controller { get; }

        public void Apply(MinimizeToTraySettings settings)
        {
            Controller.Apply(settings);
            Register(settings.Shortcut);

            if (Controller.WatchesMinimizes && _minimizes is null)
            {
                // Out-of-context, so it runs no code in other apps; this process's windows don't count.
                _minimizes = new WinEventHook(WinEventHook.MinimizeStartEvent, WinEventHook.MinimizeStartEvent, skipOwnProcess: true);
                _minimizes.Raised += (_, e) => _owner.Guard(() =>
                {
                    if (e.IsAboutWindow)
                    {
                        Controller.OnMinimizeStart(e.Window);
                    }
                });
            }
            else if (!Controller.WatchesMinimizes && _minimizes is not null)
            {
                _minimizes.Dispose();
                _minimizes = null;
            }

            LogApplied(_owner._logger, settings.Shortcut, settings.ShiftMinimize, settings.MinimizedApps.Count);
        }

        // Every window goes back on the taskbar before the icons and the window go.
        public void Dispose()
        {
            _window.MessageReceived -= OnMessageReceived;
            var released = Controller.ReleaseAll();
            if (released > 0)
            {
                LogReleased(_owner._logger, released, "Minimize to Tray stopped");
            }

            Controller.Dispose();
            _shortcut?.Dispose();
            _minimizes?.Dispose();
            _icons.Dispose();
            _system.Dispose();
            _owner._tray = null;
        }

        // Registers the shortcut again only when it changed, or when Windows refused it before.
        private void Register(MinimizeToTrayShortcut shortcut)
        {
            if (shortcut == _registered && (_shortcut is not null || shortcut == MinimizeToTrayShortcut.None))
            {
                return;
            }

            _shortcut?.Dispose();
            _shortcut = null;
            _registered = shortcut;
            switch (shortcut)
            {
                case MinimizeToTrayShortcut.None:
                    return;
                case MinimizeToTrayShortcut.WindowsShiftZ:
                    _shortcut = HotKey.Register(_window, HotKeyModifiers.Windows | HotKeyModifiers.Shift, KeyZ);
                    break;
                default:
                    LogUnknownShortcut(_owner._logger);
                    return;
            }

            if (_shortcut is null)
            {
                LogShortcutRefused(_owner._logger);
                return;
            }

            _shortcut.Pressed += (_, _) => _owner.Guard(() =>
            {
                var outcome = Controller.SendFront();
                LogPressed(_owner._logger, outcome);
            });
        }

        // At sign-out, restart and shutdown, the windows go back on the taskbar before their apps
        // are asked to close. Windows still gets the default answer, which lets the session end.
        private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message != QueryEndSession)
            {
                return;
            }

            _owner.Guard(() =>
            {
                var released = Controller.ReleaseAll();
                if (released > 0)
                {
                    LogReleased(_owner._logger, released, "the session may end");
                }
            });
        }
    }
}
