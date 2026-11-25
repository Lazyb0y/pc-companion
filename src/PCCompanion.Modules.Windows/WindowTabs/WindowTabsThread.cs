using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Runs Window Tabs on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads" in
/// docs/features/windows/window-tabs.md says: the strips must answer clicks and screen readers at
/// once, a drag reports a move at every frame, and the shortcuts and hooks need a message loop.
/// The host's UI thread starts it, passes it new settings, and stops it, and never waits for it,
/// except for <see cref="MessageLoopThread{TSettings}.Join"/> as the host exits.
/// </summary>
public sealed partial class WindowTabsThread : MessageLoopThread<WindowTabsSettings>
{
    private const string WindowClassName = "PCCompanion.WindowTabs";

    // WM_QUERYENDSESSION, which Windows sends before it asks apps to close at sign-out.
    private const uint QueryEndSession = 0x0011;

    internal const uint PageUp = 0x21;
    internal const uint PageDown = 0x22;
    internal const uint KeyT = 0x54;

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly IHostCommands _commands;
    private readonly WindowTabsWindows _shared;
    private readonly DropDownWindows _dropDown;
    private readonly ShortcutRefusals? _refusals;
    private readonly Action<WindowTabsThread>? _readKept;
    private readonly Action<WindowTabsThread, IReadOnlyList<KeptGroup>>? _saveKept;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Tabber? _tabber;
    private volatile bool _keeps;

    /// <param name="shared">The tabs as Minimize to Tray and Drop-down Apps read them.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    /// <param name="keeps">Whether the kept groups may be read and written, as <see cref="Keeps"/>.</param>
    /// <param name="readKept">Reads the kept groups off the thread as it starts, and passes them back with <see cref="StartRegrouping"/>.</param>
    /// <param name="saveKept">Writes the kept groups off the thread, after a change and as it stops.</param>
    public WindowTabsThread(
        WindowTabsSettings settings,
        TimeProvider time,
        ILogger logger,
        IHostCommands commands,
        WindowTabsWindows shared,
        DropDownWindows dropDown,
        WindowTabsThread? previous = null,
        ShortcutRefusals? refusals = null,
        bool keeps = false,
        Action<WindowTabsThread>? readKept = null,
        Action<WindowTabsThread, IReadOnlyList<KeptGroup>>? saveKept = null)
        : base("Window Tabs", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(dropDown);

        _time = time;
        _logger = logger;
        _commands = commands;
        _shared = shared;
        _dropDown = dropDown;
        _refusals = refusals;
        _keeps = keeps;
        _readKept = readKept;
        _saveKept = saveKept;
        Start();
    }

    /// <summary>
    /// Whether the kept groups may be written to <c>window-tabs.json</c>: Keep groups after a
    /// restart is on and the host doesn't run as administrator after a UAC prompt. Set it from any
    /// thread; a write checks it as it's done.
    /// </summary>
    public bool Keeps
    {
        get => _keeps;
        set => _keeps = value;
    }

    /// <summary>Makes the kept groups read from the file again, on the feature's thread, until <paramref name="until"/>.</summary>
    public void StartRegrouping(IReadOnlyList<KeptGroup?> groups, long until) => Post(() => _tabber?.StartRegrouping(groups, until));

    /// <summary>Brings the hidden tabs back on the taskbar at once, from any thread, as the host crashes.</summary>
    public void ReleaseNow() => _tabber?.Controller.ReleaseNow();

    /// <summary>
    /// Runs <paramref name="action"/> with the controller on the feature's thread, as for an action
    /// of Shortcuts &amp; Mouse Buttons, without waiting for it.
    /// </summary>
    public void Run(Action<TabsController> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Post(() =>
        {
            if (_tabber is { } tabber)
            {
                action(tabber.Controller);
            }
        });
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the controller on the feature's thread, for a click on a
    /// title bar. Returns false while the feature's thread doesn't run, as when it's starting or failed.
    /// </summary>
    public bool RunFromTitleBar(Action<TabsController> action)
    {
        if (_tabber is null)
        {
            return false;
        }

        Run(action);
        return true;
    }

    protected override IDisposable Open(MessageWindow window)
    {
        var tabber = new Tabber(this, window);
        _tabber = tabber;
        return tabber;
    }

    protected override void Apply(WindowTabsSettings settings) => _tabber!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    // Runs after the loop ends, also after a failure, and before a next thread registers the shortcuts.
    protected override void OnStopped()
    {
        _refusals?.Set(WindowTabsFeature.Definition.Id, []);
        LogStopped(_logger);
    }

    protected override void OnFailed(Exception exception)
    {
        LogFailed(_logger, exception);

        // A failure brings the hidden tabs back, as stopping does.
        try
        {
            _tabber?.Controller.ReleaseNow();
        }
        catch (Exception release)
        {
            LogReleaseFailed(_logger, release);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs applied its settings: group by dropping {GroupByDropping}, switch shortcuts {SwitchShortcuts}, group shortcuts {GroupShortcuts}, keep groups {KeepGroups}.")]
    private static partial void LogApplied(ILogger logger, bool groupByDropping, TabSwitchShortcuts switchShortcuts, TabGroupShortcuts groupShortcuts, bool keepGroups);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs brought {Count} hidden tabs back on the taskbar: {Why}.")]
    private static partial void LogReleased(ILogger logger, int count, string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Window Tabs' {Shortcut} shortcut; another program may have it.")]
    private static partial void LogShortcutRefused(ILogger logger, string shortcut);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Tabs switch shortcuts in the settings are unknown, so there are none.")]
    private static partial void LogUnknownShortcuts(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Tabs group shortcuts in the settings are unknown, so there are none.")]
    private static partial void LogUnknownGroupShortcuts(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Tabs failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Tabs couldn't bring its hidden tabs back on the taskbar after a failure.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller, its hooks, the strips and the shortcuts.
    private sealed class Tabber : IDisposable
    {
        // How long after a change the kept groups are written.
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

        private readonly WindowTabsThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsTabsSystem _system;
        private readonly DropHighlight _highlight;
        private readonly List<WinEventHook> _hooks = [];
        private readonly List<HotKey> _switchShortcuts = [];
        private readonly List<HotKey> _groupShortcuts = [];
        private TabSwitchShortcuts? _switchRegistered;
        private TabGroupShortcuts? _groupRegistered;
        private bool _switchRefused;
        private bool _groupRefused;
        private WinEventHook? _newWindows;
        private ITimer? _save;
        private bool _unsaved;

        public Tabber(WindowTabsThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
            Action<Action> post = action => window.BeginInvoke(() => owner.Guard(action));
            _system = new WindowsTabsSystem(window, owner.Guard, owner._dropDown);
            _highlight = new DropHighlight();
            Controller = new TabsController(
                _system,
                () => new TabStrip(window, owner.Guard, rightToLeft),
                _highlight,
                owner._time,
                post,
                owner._logger,
                owner._shared,
                rightToLeft,
                (index, count) => string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.WindowTabsTabPosition), index, count),
                () => owner._commands.OpenFeatureSettings(WindowTabsFeature.Definition.Id));

            // Out-of-context hooks for every process, which run no code in other apps.
            Hook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, (_, window) => Controller.OnForeground(window));
            Hook(WinEventHook.MoveSizeStartEvent, WinEventHook.MoveSizeEndEvent, (type, window) =>
            {
                if (type == WinEventHook.MoveSizeStartEvent)
                {
                    Controller.OnMoveSizeStart(window);
                }
                else
                {
                    Controller.OnMoveSizeEnd(window);
                }
            });
            Hook(WinEventHook.MinimizeStartEvent, WinEventHook.MinimizeEndEvent, (type, window) =>
            {
                if (type == WinEventHook.MinimizeStartEvent)
                {
                    Controller.OnMinimizeStart(window);
                }
                else
                {
                    Controller.OnMinimizeEnd(window);
                }
            });
            Hook(WinEventHook.ObjectCloakedEvent, WinEventHook.ObjectUncloakedEvent, (_, window) => Controller.OnCloakChanged(window));

            _system.WindowShown += (_, shown) => Controller.OnShown(shown);
            _system.WindowHidden += (_, hidden) => Controller.OnHidden(hidden);
            _system.WindowDestroyed += (_, destroyed) => Controller.OnDestroyed(destroyed);
            _system.TitleChanged += (_, changed) => Controller.OnTitleChanged(changed);
            _system.WindowMoved += (_, moved) => Controller.OnLocationChange(moved);
            _window.MessageReceived += OnMessageReceived;
            Controller.KeptChanged += (_, _) => ScheduleSave();
            Controller.RegroupingEnded += (_, _) => StopWatchingNewWindows();

            if (owner.Keeps)
            {
                owner._readKept?.Invoke(owner);
            }
        }

        public TabsController Controller { get; }

        public void Apply(WindowTabsSettings settings)
        {
            Controller.Apply(settings);
            RegisterSwitch(settings.SwitchShortcuts);
            RegisterGroup(settings.GroupShortcuts);
            _owner._refusals?.Set(WindowTabsFeature.Definition.Id, Refused());
            LogApplied(_owner._logger, settings.GroupByDropping, settings.SwitchShortcuts, settings.GroupShortcuts, settings.KeepGroups);
        }

        public void StartRegrouping(IReadOnlyList<KeptGroup?> groups, long until)
        {
            Controller.StartRegrouping(groups, until);
            if (Controller.IsRegrouping && _newWindows is null)
            {
                // A window that shows in any app may be one a kept group waits for; out of
                // context, the hook runs no code in other apps.
                var hook = new WinEventHook(WinEventHook.ObjectShowEvent, WinEventHook.ObjectShowEvent, skipOwnProcess: true);
                hook.Raised += (_, e) => _owner.Guard(() =>
                {
                    if (e.IsAboutWindow)
                    {
                        Controller.OnNewWindow(e.Window);
                    }
                });
                _newWindows = hook;
            }
        }

        // Every hidden tab goes back on the taskbar before the hooks and the window go, and the
        // groups stay kept.
        public void Dispose()
        {
            _window.MessageReceived -= OnMessageReceived;
            var released = Controller.ReleaseAll();
            if (released > 0)
            {
                LogReleased(_owner._logger, released, "Window Tabs stopped");
            }

            SaveNow();
            _save?.Dispose();
            StopWatchingNewWindows();

            Controller.Dispose();
            Unregister(_switchShortcuts);
            Unregister(_groupShortcuts);

            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }

            _highlight.Dispose();
            _system.Dispose();
            _owner._tabber = null;
        }

        private void StopWatchingNewWindows()
        {
            _newWindows?.Dispose();
            _newWindows = null;
        }

        private void ScheduleSave()
        {
            _unsaved = true;
            if (_save is null && _owner.Keeps)
            {
                _save = _owner._time.CreateTimer(_ => _window.BeginInvoke(() => _owner.Guard(SaveNow)), null, SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private void SaveNow()
        {
            _save?.Dispose();
            _save = null;
            if (!_unsaved || !_owner.Keeps)
            {
                return;
            }

            _unsaved = false;
            _owner._saveKept?.Invoke(_owner, Controller.Kept);
        }

        private void Hook(uint first, uint last, Action<uint, nint> handle)
        {
            var hook = new WinEventHook(first, last, skipOwnProcess: true);
            hook.Raised += (_, e) => _owner.Guard(() =>
            {
                if (e.IsAboutWindow)
                {
                    handle(e.EventType, e.Window);
                }
            });
            _hooks.Add(hook);
        }

        private static void Unregister(List<HotKey> shortcuts)
        {
            foreach (var shortcut in shortcuts)
            {
                shortcut.Dispose();
            }

            shortcuts.Clear();
        }

        private List<string> Refused()
        {
            var refused = new List<string>();
            if (_switchRefused)
            {
                refused.Add(WindowTabsFeature.SwitchShortcutsName);
            }

            if (_groupRefused)
            {
                refused.Add(WindowTabsFeature.GroupShortcutsName);
            }

            return refused;
        }

        // The shortcuts are registered again only when they changed, or when Windows refused one before.
        private void RegisterSwitch(TabSwitchShortcuts shortcuts)
        {
            if (shortcuts == _switchRegistered && !_switchRefused)
            {
                return;
            }

            Unregister(_switchShortcuts);
            _switchRegistered = shortcuts;
            _switchRefused = false;
            var keys = shortcuts switch
            {
                TabSwitchShortcuts.WindowsControlPage => HotKeyModifiers.Windows | HotKeyModifiers.Control,
                TabSwitchShortcuts.WindowsAltPage => HotKeyModifiers.Windows | HotKeyModifiers.Alt,
                _ => (HotKeyModifiers?)null,
            };

            if (keys is not { } modifiers)
            {
                if (shortcuts != TabSwitchShortcuts.None)
                {
                    LogUnknownShortcuts(_owner._logger);
                }

                return;
            }

            _switchRefused |= !Add(_switchShortcuts, modifiers, PageDown, "next tab", Controller.Next);
            _switchRefused |= !Add(_switchShortcuts, modifiers, PageUp, "previous tab", Controller.Previous);
            _switchRefused |= !Add(_switchShortcuts, modifiers | HotKeyModifiers.Shift, PageDown, "move tab forward", Controller.MoveForward);
            _switchRefused |= !Add(_switchShortcuts, modifiers | HotKeyModifiers.Shift, PageUp, "move tab back", Controller.MoveBack);
        }

        private void RegisterGroup(TabGroupShortcuts shortcuts)
        {
            if (shortcuts == _groupRegistered && !_groupRefused)
            {
                return;
            }

            Unregister(_groupShortcuts);
            _groupRegistered = shortcuts;
            _groupRefused = false;
            if (shortcuts != TabGroupShortcuts.WindowsControlT)
            {
                if (shortcuts != TabGroupShortcuts.None)
                {
                    LogUnknownGroupShortcuts(_owner._logger);
                }

                return;
            }

            var modifiers = HotKeyModifiers.Windows | HotKeyModifiers.Control;
            _groupRefused |= !Add(_groupShortcuts, modifiers, KeyT, "add to the group behind", Controller.GroupFrontWithBehind);
            _groupRefused |= !Add(_groupShortcuts, modifiers | HotKeyModifiers.Shift, KeyT, "take out of group", Controller.TakeOutFront);
        }

        private bool Add(List<HotKey> shortcuts, HotKeyModifiers modifiers, uint key, string name, Action press)
        {
            var hotKey = HotKey.Register(_window, modifiers, key);
            if (hotKey is null)
            {
                LogShortcutRefused(_owner._logger, name);
                return false;
            }

            hotKey.Pressed += (_, _) => _owner.Guard(press);
            shortcuts.Add(hotKey);
            return true;
        }

        // At sign-out, restart and shutdown, the hidden tabs go back on the taskbar before their
        // apps are asked to close. Windows still gets the default answer, which lets the session end.
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

                SaveNow();
            });
        }
    }
}
