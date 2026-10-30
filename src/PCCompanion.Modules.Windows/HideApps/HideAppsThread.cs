using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Runs Hide Apps on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads" in
/// docs/features/windows/hide-apps.md says: a press lists and minimizes every window, and bringing
/// windows back waits for each in turn. The host's UI thread starts it, passes it new settings and
/// the PC Companion icon's menu item, and stops it, and never waits for it, except for
/// <see cref="MessageLoopThread{TSettings}.Join"/> as the host exits.
/// </summary>
public sealed partial class HideAppsThread : MessageLoopThread<HideAppsSettings>
{
    private const string WindowClassName = "PCCompanion.HideApps";

    // The H key.
    private const uint KeyH = 0x48;

    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Hider? _hider;

    public HideAppsThread(HideAppsSettings settings, TimeProvider time, ILogger logger, HideAppsThread? previous = null)
        : base("Hide Apps", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _time = time;
        _logger = logger;
        Start();
    }

    /// <summary>How many windows Hide Apps can bring back, for the PC Companion icon's menu; any thread can read it.</summary>
    public int Count => _hider?.Controller.Count ?? 0;

    /// <summary>Brings back every window that Hide Apps minimized, on the feature's thread.</summary>
    public void ShowAll() => Post(() => _hider?.Press(HideAction.ShowAll));

    protected override IDisposable Open(MessageWindow window)
    {
        var hider = new Hider(this, window);
        _hider = hider;
        return hider;
    }

    protected override void Apply(HideAppsSettings settings) => _hider!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hide Apps applied its settings: Hide app {HideApp}, Hide others {HideOthers}, display in front only {FrontDisplayOnly}, whole apps {BringBackWholeApp}.")]
    private static partial void LogApplied(ILogger logger, HideAppShortcut hideApp, HideOthersShortcut hideOthers, bool frontDisplayOnly, bool bringBackWholeApp);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Hide Apps' {Action} shortcut; another program may have it.")]
    private static partial void LogShortcutRefused(ILogger logger, HideAction action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Hide Apps {Action} shortcut in the settings is unknown, so it has no shortcut.")]
    private static partial void LogUnknownShortcut(ILogger logger, HideAction action);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hide Apps failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller, its hooks and the shortcuts.
    private sealed class Hider : IDisposable
    {
        private readonly HideAppsThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsHideAppsSystem _system;
        private HotKey? _hideApp;
        private HotKey? _hideOthers;
        private HideAppShortcut? _registeredHideApp;
        private HideOthersShortcut? _registeredHideOthers;

        public Hider(HideAppsThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            _system = new WindowsHideAppsSystem(owner.Guard);
            Controller = new HideAppsController(_system, owner._time, action => window.BeginInvoke(() => owner.Guard(action)), owner._logger);

            _system.WindowRestored += (_, restored) => Controller.OnRestored(restored);
            _system.WindowDestroyed += (_, destroyed) => Controller.OnDestroyed(destroyed);
            _system.OrderChanged += (_, _) => Controller.OnOrderChanged();
        }

        public HideAppsController Controller { get; }

        public void Apply(HideAppsSettings settings)
        {
            Controller.Apply(settings);
            RegisterHideApp(settings.HideAppShortcut);
            RegisterHideOthers(settings.HideOthersShortcut);
            LogApplied(_owner._logger, settings.HideAppShortcut, settings.HideOthersShortcut, settings.FrontDisplayOnly, settings.BringBackWholeApp);
        }

        public void Press(HideAction action) => _ = action switch
        {
            HideAction.HideApp => Controller.HideApp(),
            HideAction.HideOthers => Controller.HideOthers(),
            _ => Controller.ShowAll(),
        };

        // The windows stay as they are: minimized windows are on the taskbar.
        public void Dispose()
        {
            Controller.Dispose();
            _hideApp?.Dispose();
            _hideOthers?.Dispose();
            _system.Dispose();
            _owner._hider = null;
        }

        // Each shortcut is registered again only when it changed, or when Windows refused it before.
        private void RegisterHideApp(HideAppShortcut shortcut)
        {
            if (shortcut == _registeredHideApp && (_hideApp is not null || shortcut == HideAppShortcut.None))
            {
                return;
            }

            _hideApp?.Dispose();
            _registeredHideApp = shortcut;
            _hideApp = shortcut switch
            {
                HideAppShortcut.None => null,
                HideAppShortcut.WindowsControlH => Register(HotKeyModifiers.Windows | HotKeyModifiers.Control, HideAction.HideApp),
                _ => Unknown(HideAction.HideApp),
            };
        }

        private void RegisterHideOthers(HideOthersShortcut shortcut)
        {
            if (shortcut == _registeredHideOthers && (_hideOthers is not null || shortcut == HideOthersShortcut.None))
            {
                return;
            }

            _hideOthers?.Dispose();
            _registeredHideOthers = shortcut;
            _hideOthers = shortcut switch
            {
                HideOthersShortcut.None => null,
                HideOthersShortcut.WindowsControlShiftH => Register(HotKeyModifiers.Windows | HotKeyModifiers.Control | HotKeyModifiers.Shift, HideAction.HideOthers),
                _ => Unknown(HideAction.HideOthers),
            };
        }

        private HotKey? Register(HotKeyModifiers modifiers, HideAction action)
        {
            var hotKey = HotKey.Register(_window, modifiers, KeyH);
            if (hotKey is null)
            {
                LogShortcutRefused(_owner._logger, action);
                return null;
            }

            hotKey.Pressed += (_, _) => _owner.Guard(() => Press(action));
            return hotKey;
        }

        private HotKey? Unknown(HideAction action)
        {
            LogUnknownShortcut(_owner._logger, action);
            return null;
        }
    }
}
