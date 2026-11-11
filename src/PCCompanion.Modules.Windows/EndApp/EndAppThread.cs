using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// Runs End App on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads" in
/// docs/features/windows/end-app.md says: while the user chooses, every pointer move and key is
/// handled at once, and the shortcut needs a message loop. The host's UI thread starts it, passes
/// it new settings and the menu item's choice, and stops it, and never waits for it, except for
/// <see cref="MessageLoopThread{TSettings}.Join"/> as the host exits.
/// </summary>
public sealed partial class EndAppThread : MessageLoopThread<EndAppSettings>
{
    private const string WindowClassName = "PCCompanion.EndApp";
    private const uint KeyEscape = 0x1B;

    // The frame's colors outside high contrast, as 0xRRGGBB.
    private const uint CanEndColor = 0xE81123;
    private const uint CantEndColor = 0x8A8A8A;

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ShortcutRefusals? _refusals;
    private readonly Action<string, string> _notify;

    // Set on the feature's thread while its message loop runs.
    private volatile Runner? _runner;

    /// <param name="notify">Shows a notification from the PC Companion icon, from any thread.</param>
    public EndAppThread(EndAppSettings settings, TimeProvider time, ILogger logger, Action<string, string> notify, EndAppThread? previous = null, ShortcutRefusals? refusals = null)
        : base("End App", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(notify);

        _time = time;
        _logger = logger;
        _notify = notify;
        _refusals = refusals;
        Start();
    }

    /// <summary>Starts choosing an app, for the PC Companion icon's menu item, on the feature's thread.</summary>
    public void StartFromMenu() => Post(() => _runner?.Controller.Start(EndAppSource.Menu));

    protected override IDisposable Open(MessageWindow window)
    {
        var runner = new Runner(this, window);
        _runner = runner;
        return runner;
    }

    protected override void Apply(EndAppSettings settings) => _runner!.Apply(settings);

    protected override void OnStopping() => _runner?.Controller.Dispose();

    protected override void OnStarted() => LogStarted(_logger);

    // Runs after the loop ends, also after a failure, and before a next thread registers the shortcuts.
    protected override void OnStopped()
    {
        _refusals?.Set(EndAppFeature.Definition.Id, []);
        LogStopped(_logger);
    }

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App's thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App applied its settings: shortcut {Shortcut}.")]
    private static partial void LogApplied(ILogger logger, EndAppShortcut shortcut);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused End App's shortcut; another program may have it.")]
    private static partial void LogShortcutRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The End App shortcut in the settings is unknown, so it has no shortcut.")]
    private static partial void LogUnknownShortcut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "End App's question couldn't be shown.")]
    private static partial void LogQuestionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "End App couldn't open Task Manager.")]
    private static partial void LogTaskManagerNotOpened(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "End App failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // What runs on the feature's thread: the controller, its windows and the shortcut.
    private sealed class Runner : IDisposable, IEndAppView, IEndAppHost
    {
        private readonly EndAppThread _owner;
        private readonly MessageWindow _window;
        private readonly CoveringWindow _covering;
        private readonly WindowFrame _frame;
        private HotKey? _hotKey;
        private EndAppShortcut? _registered;

        public Runner(EndAppThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            _covering = new CoveringWindow(WindowClassName + ".Covering", Strings.EndAppChoosing);
            _frame = new WindowFrame(WindowClassName + ".Frame");
            Controller = new EndAppController(new WindowsEndAppSystem(), this, this, owner._time, Post, owner._logger);

            _covering.PointerMoved += (_, e) => owner.Guard(() => Controller.OnPointerMoved(e.Point));
            _covering.Clicked += (_, e) => owner.Guard(() => Controller.OnClicked(e.Point, e.IsRight));
            _covering.KeyPressed += (_, e) => owner.Guard(() => Controller.OnKey(e.VirtualKey, e.Shift));
            _covering.Deactivated += (_, _) => owner.Guard(Controller.OnDeactivated);
            _covering.DisplaysChanged += (_, _) => owner.Guard(Controller.OnDisplaysChanged);
        }

        public EndAppController Controller { get; }

        public bool IsCovering => _covering.IsShown;

        public void Apply(EndAppSettings settings)
        {
            Register(settings.Shortcut);
            var refused = settings.Shortcut is EndAppShortcut.ControlAltEscape or EndAppShortcut.WindowsControlEscape && _hotKey is null;
            _owner._refusals?.Set(EndAppFeature.Definition.Id, refused ? [EndAppFeature.ChooseAppShortcut] : []);
            LogApplied(_owner._logger, settings.Shortcut);
        }

        public bool ShowCovering() => _covering.Show();

        public void HideCovering() => _covering.Hide();

        public void SetPointer(bool canEnd) => _covering.PointerShape = canEnd ? CoveringPointer.Cross : CoveringPointer.Unavailable;

        public void SetTitle(string title) => _covering.SetTitle(title);

        public void ShowFrame(Rectangle bounds, int thickness, bool canEnd)
        {
            var color = WindowsTheme.IsHighContrast()
                ? (canEnd ? WindowsTheme.HighlightColor() : WindowsTheme.GrayTextColor())
                : (canEnd ? CanEndColor : CantEndColor);
            _frame.Show(bounds, thickness, color);
        }

        public void HideFrame() => _frame.Hide();

        // The texts come from this thread's language.
        public IEndAppQuestion Ask(QuestionPage page, AppIcon? icon, Rectangle? area, Action<int> answered)
        {
            var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
            var logger = _owner._logger;
            var dialog = QuestionDialog.Show(Strings.EndAppName, page, icon, area, rightToLeft, answered, exception =>
            {
                LogQuestionFailed(logger, exception);
                answered(-1);
            });
            return new Question(dialog);
        }

        public void OpenTaskManager()
        {
            var logger = _owner._logger;
            Task.Run(() =>
            {
                if (!ShellActions.OpenTaskManager())
                {
                    LogTaskManagerNotOpened(logger);
                }
            });
        }

        public void Notify(string title, string text) => _owner._notify(title, text);

        // A failure on the thread pool fails the feature on its own thread, as one there would.
        public void Background(Action work) => Task.Run(() =>
        {
            try
            {
                work();
            }
            catch (Exception exception)
            {
                Post(() => ExceptionDispatchInfo.Throw(exception));
            }
        });

        public void Dispose()
        {
            Controller.Dispose();
            _hotKey?.Dispose();
            _frame.Dispose();
            _covering.Dispose();
            _owner._runner = null;
        }

        private void Post(Action action) => _window.BeginInvoke(() => _owner.Guard(action));

        // Registered again only when it changed, or when Windows refused it before.
        private void Register(EndAppShortcut shortcut)
        {
            if (shortcut == _registered && (_hotKey is not null || shortcut == EndAppShortcut.None))
            {
                return;
            }

            _hotKey?.Dispose();
            _hotKey = null;
            _registered = shortcut;
            var modifiers = shortcut switch
            {
                EndAppShortcut.ControlAltEscape => HotKeyModifiers.Control | HotKeyModifiers.Alt,
                EndAppShortcut.WindowsControlEscape => HotKeyModifiers.Windows | HotKeyModifiers.Control,
                _ => HotKeyModifiers.None,
            };

            if (shortcut == EndAppShortcut.None)
            {
                return;
            }

            if (modifiers == HotKeyModifiers.None)
            {
                LogUnknownShortcut(_owner._logger);
                return;
            }

            _hotKey = HotKey.Register(_window, modifiers, KeyEscape);
            if (_hotKey is null)
            {
                LogShortcutRefused(_owner._logger);
                return;
            }

            _hotKey.Pressed += (_, _) => _owner.Guard(() => Controller.Start(EndAppSource.Shortcut));
        }
    }

    private sealed class Question(QuestionDialog dialog) : IEndAppQuestion
    {
        public void Change(QuestionPage page) => dialog.Change(page);

        public void Close() => dialog.Close();
    }
}
