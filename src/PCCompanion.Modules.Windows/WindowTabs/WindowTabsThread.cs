using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

    private const uint PageUp = 0x21;
    private const uint PageDown = 0x22;

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly IHostCommands _commands;
    private readonly WindowTabsWindows _shared;
    private readonly DropDownWindows _dropDown;
    private readonly ShortcutRefusals? _refusals;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Tabber? _tabber;

    /// <param name="shared">The tabs as Minimize to Tray and Drop-down Apps read them.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    public WindowTabsThread(WindowTabsSettings settings, TimeProvider time, ILogger logger, IHostCommands commands, WindowTabsWindows shared, DropDownWindows dropDown, WindowTabsThread? previous = null, ShortcutRefusals? refusals = null)
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
        Start();
    }

    /// <summary>Brings the hidden tabs back on the taskbar at once, from any thread, as the host crashes.</summary>
    public void ReleaseNow() => _tabber?.Controller.ReleaseNow();

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs applied its settings: group by dropping {GroupByDropping}, switch shortcuts {SwitchShortcuts}.")]
    private static partial void LogApplied(ILogger logger, bool groupByDropping, TabSwitchShortcuts switchShortcuts);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs brought {Count} hidden tabs back on the taskbar: {Why}.")]
    private static partial void LogReleased(ILogger logger, int count, string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Window Tabs' {Shortcut} shortcut; another program may have it.")]
    private static partial void LogShortcutRefused(ILogger logger, string shortcut);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Tabs switch shortcuts in the settings are unknown, so there are none.")]
    private static partial void LogUnknownShortcuts(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Tabs failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Tabs couldn't bring its hidden tabs back on the taskbar after a failure.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller, its hooks, the strips and the shortcuts.
    private sealed class Tabber : IDisposable
    {
        private readonly WindowTabsThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsTabsSystem _system;
        private readonly DropHighlight _highlight;
        private readonly List<WinEventHook> _hooks = [];
        private readonly List<HotKey> _shortcuts = [];
        private TabSwitchShortcuts? _registered;

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
        }

        public TabsController Controller { get; }

        public void Apply(WindowTabsSettings settings)
        {
            Controller.Apply(settings);
            Register(settings.SwitchShortcuts);
            LogApplied(_owner._logger, settings.GroupByDropping, settings.SwitchShortcuts);
        }

        // Every hidden tab goes back on the taskbar before the hooks and the window go.
        public void Dispose()
        {
            _window.MessageReceived -= OnMessageReceived;
            var released = Controller.ReleaseAll();
            if (released > 0)
            {
                LogReleased(_owner._logger, released, "Window Tabs stopped");
            }

            Controller.Dispose();
            foreach (var shortcut in _shortcuts)
            {
                shortcut.Dispose();
            }

            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }

            _highlight.Dispose();
            _system.Dispose();
            _owner._tabber = null;
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

        // The shortcuts are registered again only when they changed, or when Windows refused one before.
        private void Register(TabSwitchShortcuts shortcuts)
        {
            if (shortcuts == _registered && (_shortcuts.Count == 4 || shortcuts == TabSwitchShortcuts.None))
            {
                return;
            }

            foreach (var shortcut in _shortcuts)
            {
                shortcut.Dispose();
            }

            _shortcuts.Clear();
            _registered = shortcuts;
            var keys = shortcuts switch
            {
                TabSwitchShortcuts.None => (HotKeyModifiers?)null,
                TabSwitchShortcuts.WindowsControlPage => HotKeyModifiers.Windows | HotKeyModifiers.Control,
                TabSwitchShortcuts.WindowsAltPage => HotKeyModifiers.Windows | HotKeyModifiers.Alt,
                _ => null,
            };

            if (keys is not { } modifiers)
            {
                if (shortcuts != TabSwitchShortcuts.None)
                {
                    LogUnknownShortcuts(_owner._logger);
                }

                _owner._refusals?.Set(WindowTabsFeature.Definition.Id, []);
                return;
            }

            var refused = false;
            refused |= !Add(modifiers, PageDown, "next tab", Controller.Next);
            refused |= !Add(modifiers, PageUp, "previous tab", Controller.Previous);
            refused |= !Add(modifiers | HotKeyModifiers.Shift, PageDown, "move tab forward", Controller.MoveForward);
            refused |= !Add(modifiers | HotKeyModifiers.Shift, PageUp, "move tab back", Controller.MoveBack);
            _owner._refusals?.Set(WindowTabsFeature.Definition.Id, refused ? [WindowTabsFeature.SwitchShortcutsName] : []);
        }

        private bool Add(HotKeyModifiers modifiers, uint key, string name, Action press)
        {
            var hotKey = HotKey.Register(_window, modifiers, key);
            if (hotKey is null)
            {
                LogShortcutRefused(_owner._logger, name);
                return false;
            }

            hotKey.Pressed += (_, _) => _owner.Guard(press);
            _shortcuts.Add(hotKey);
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
            });
        }
    }
}
