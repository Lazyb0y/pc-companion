using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// Runs Drop-down Apps on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads"
/// in docs/features/windows/drop-down-apps.md says: a slide waits for each frame for about 150 ms,
/// and the shortcuts and hooks need a message loop. The host's UI thread starts it, passes it new
/// settings, and stops it, and never waits for it, except for
/// <see cref="MessageLoopThread{TSettings}.Join"/> as the host exits.
/// </summary>
public sealed partial class DropDownAppsThread : MessageLoopThread<DropDownAppsSettings>
{
    private const string WindowClassName = "PCCompanion.DropDownApps";

    // WM_QUERYENDSESSION, which Windows sends before it asks apps to close at sign-out.
    private const uint QueryEndSession = 0x0011;

    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ShortcutRefusals? _refusals;
    private readonly DropDownWindows _shared;
    private readonly DropDownResults _results;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Dropper? _dropper;

    /// <param name="shared">The drop-down windows as Hide Apps and Minimize to Tray read them.</param>
    /// <param name="results">The recent results, which the feature gives the settings app.</param>
    public DropDownAppsThread(DropDownAppsSettings settings, IHostCommands commands, TimeProvider time, ILogger logger, DropDownWindows shared, DropDownResults results, DropDownAppsThread? previous = null, ShortcutRefusals? refusals = null)
        : base("Drop-down Apps", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(results);

        _commands = commands;
        _time = time;
        _logger = logger;
        _shared = shared;
        _results = results;
        _refusals = refusals;
        Start();
    }

    /// <summary>Brings the hidden windows back on the taskbar at once, from any thread, as the host crashes.</summary>
    public void ReleaseNow() => _dropper?.Controller.ReleaseNow();

    /// <summary>Does what the shortcut of the listed app that <paramref name="target"/> names does, on the feature's thread.</summary>
    public void PressApp(string target) => Post(() => _dropper?.Controller.PressApp(target));

    protected override IDisposable Open(MessageWindow window)
    {
        var dropper = new Dropper(this, window);
        _dropper = dropper;
        return dropper;
    }

    protected override void Apply(DropDownAppsSettings settings) => _dropper!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    // Runs after the loop ends, also after a failure, and before a next thread registers the shortcuts.
    protected override void OnStopped()
    {
        _refusals?.Set(DropDownAppsFeature.Definition.Id, []);
        LogStopped(_logger);
    }

    protected override void OnFailed(Exception exception)
    {
        LogFailed(_logger, exception);

        // A failure lets the windows go, as stopping does.
        try
        {
            _dropper?.Controller.ReleaseNow();
        }
        catch (Exception release)
        {
            LogReleaseFailed(_logger, release);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps applied its settings: {Apps} listed apps, {Shortcuts} shortcuts, display {Display}, width {Width}%, height {Height}%, slide {Slide}, hide {HideWhenInactive}, keep on top {KeepOnTop}, layout checks {ChecksLayout}.")]
    private static partial void LogApplied(ILogger logger, int apps, int shortcuts, DropDownDisplay display, int width, int height, bool slide, HideWhenInactive hideWhenInactive, bool keepOnTop, bool checksLayout);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps let {Count} hidden windows go: {Why}.")]
    private static partial void LogReleased(ILogger logger, int count, string why);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drop-down Apps failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drop-down Apps couldn't bring its windows back on the taskbar after a failure.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller, its hooks and the shortcuts.
    private sealed class Dropper : IDisposable
    {
        private readonly DropDownAppsThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsDropDownSystem _system;
        private readonly ShortcutKeeper _shortcuts;
        private readonly WinEventHook _foreground;

        public Dropper(DropDownAppsThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            Action<Action> post = action => window.BeginInvoke(() => owner.Guard(action));
            _system = new WindowsDropDownSystem(window, owner.Guard, owner._commands);
            Controller = new DropDownController(_system, owner._time, post, owner._logger, owner._shared);
            _shortcuts = new ShortcutKeeper(new WindowsShortcutEnvironment(window), owner._time, post);
            ShortcutKeeperLog.Attach(_shortcuts, owner._logger, "Drop-down Apps");

            // Another program's window coming to the front: the keyboard layout may change, and a
            // shown drop-down window may hide. Out-of-context, so it runs no code in other apps.
            _foreground = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);

            _system.WindowShown += (_, shown) => Controller.OnShown(shown);
            _system.WindowDestroyed += (_, destroyed) => Controller.OnDestroyed(destroyed);
            _system.MinimizeStarted += (_, minimizing) => Controller.OnMinimizeStart(minimizing);
            Controller.Reported += (_, report) => owner._results.Add(report.AppIndex, report.Outcome);
            _shortcuts.Pressed += (_, keys) => owner.Guard(() => Press(keys));
            _shortcuts.Refused += (_, refused) => owner.Guard(() => Refused(refused.Shortcut));
            _shortcuts.RefusedShortcutsChanged += (_, _) => owner.Guard(ReportRefused);
            _foreground.Raised += (_, _) => owner.Guard(() =>
            {
                _shortcuts.Check();
                Controller.OnForeground();
            });
            _window.MessageReceived += OnMessageReceived;
        }

        public DropDownController Controller { get; }

        public void Apply(DropDownAppsSettings settings)
        {
            Controller.Apply(settings);
            var keys = Controller.Shortcuts.Select(DropDownShortcuts.KeysOf).OfType<KeyPositionShortcut>().ToList();
            _shortcuts.Configure(keys);
            LogApplied(
                _owner._logger,
                settings.Apps.Count,
                keys.Count,
                settings.Display,
                settings.Width,
                settings.Height,
                settings.Slide,
                settings.HideWhenInactive,
                settings.KeepOnTop,
                _shortcuts.ChecksLayout);
        }

        // Every window goes back on the taskbar before the hooks and the window go.
        public void Dispose()
        {
            _window.MessageReceived -= OnMessageReceived;
            var released = Controller.ReleaseAll();
            if (released > 0)
            {
                LogReleased(_owner._logger, released, "Drop-down Apps stopped");
            }

            Controller.Dispose();
            _shortcuts.Dispose();
            _foreground.Dispose();
            _system.Dispose();
            _owner._dropper = null;
        }

        private static DropDownShortcut ShortcutOf(KeyPositionShortcut keys) =>
            DropDownShortcuts.All.FirstOrDefault(shortcut => DropDownShortcuts.KeysOf(shortcut) == keys, DropDownShortcut.None);

        private void Press(KeyPositionShortcut keys) => Controller.Press(ShortcutOf(keys));

        // The settings app shows a note at each app whose shortcut Windows refused, named as in the settings.
        private void ReportRefused() =>
            _owner._refusals?.Set(DropDownAppsFeature.Definition.Id, _shortcuts.RefusedShortcuts.Select(ShortcutOf).Where(shortcut => shortcut != DropDownShortcut.None).Select(shortcut => shortcut.ToString()));

        // Another program has the shortcut, which the recent results say for the app that has it.
        private void Refused(KeyPositionShortcut keys)
        {
            var shortcut = ShortcutOf(keys);
            for (var index = 0; index < Controller.Shortcuts.Count; index++)
            {
                if (Controller.Shortcuts[index] == shortcut && shortcut != DropDownShortcut.None)
                {
                    _owner._results.Add(index, DropDownOutcome.ShortcutTaken);
                }
            }
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
