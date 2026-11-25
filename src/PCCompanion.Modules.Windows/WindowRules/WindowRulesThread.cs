using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Runs Window Rules on a <see cref="MessageLoopThread{TSettings}"/> of its own, as "Threads" in
/// docs/features/windows/window-rules.md says: it hears every window that shows, reads each one's
/// app, and its hooks need a message loop. The host's UI thread starts it, passes it new settings,
/// and stops it, and never waits for it, except for <see cref="MessageLoopThread{TSettings}.Join"/>
/// as the host exits.
/// </summary>
public sealed partial class WindowRulesThread : MessageLoopThread<WindowRulesSettings>
{
    private const string WindowClassName = "PCCompanion.WindowRules";

    // WM_QUERYENDSESSION, which Windows sends before it asks apps to close at sign-out.
    private const uint QueryEndSession = 0x0011;

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly WindowRequests _requests;
    private readonly string? _pipeName;
    private readonly Action<WindowRulesThread>? _readRemembered;
    private readonly Action<WindowRulesThread, IReadOnlyList<RememberedRule>>? _saveRemembered;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Ruler? _ruler;
    private volatile bool _keeps;

    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    /// <param name="requests">Where windows go to Window Tabs for Open as a tab of.</param>
    /// <param name="pipeName">The pipe the settings app asks through for Apply now; none in tests.</param>
    /// <param name="keeps">Whether what rules remember may be read and written, as <see cref="Keeps"/>.</param>
    /// <param name="readRemembered">Reads what rules remember off the thread as it starts, and passes it back with <see cref="LoadRemembered"/>.</param>
    /// <param name="saveRemembered">Writes what rules remember off the thread, after a change and as it stops.</param>
    public WindowRulesThread(
        WindowRulesSettings settings,
        TimeProvider time,
        ILogger logger,
        DropDownWindows dropDown,
        WindowTabsWindows tabs,
        WindowRequests requests,
        WindowRulesThread? previous = null,
        string? pipeName = null,
        bool keeps = false,
        Action<WindowRulesThread>? readRemembered = null,
        Action<WindowRulesThread, IReadOnlyList<RememberedRule>>? saveRemembered = null)
        : base("Window Rules", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(requests);

        _time = time;
        _logger = logger;
        _dropDown = dropDown;
        _tabs = tabs;
        _requests = requests;
        _pipeName = pipeName;
        _keeps = keeps;
        _readRemembered = readRemembered;
        _saveRemembered = saveRemembered;
        Start();
    }

    /// <summary>
    /// Whether what rules remember may be written to <c>window-rules.json</c>: the host doesn't run
    /// as administrator after a UAC prompt, and the feature is on. Set it from any thread; a write
    /// checks it as it's done.
    /// </summary>
    public bool Keeps
    {
        get => _keeps;
        set => _keeps = value;
    }

    /// <summary>Adds what the file kept, on the feature's thread.</summary>
    public void LoadRemembered(IReadOnlyList<RememberedRule?> rules) => Post(() => _ruler?.Controller.Remembered.Load(rules));

    /// <summary>Title Bar Actions' wheel gave a window an opacity; any thread can call it.</summary>
    public void ReportWheelOpacity(nint window, int percent) => Post(() => _ruler?.Controller.OnWheelOpacity(window, percent));

    protected override IDisposable Open(MessageWindow window)
    {
        var ruler = new Ruler(this, window);
        _ruler = ruler;
        return ruler;
    }

    protected override void Apply(WindowRulesSettings settings) => _ruler!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    /// <summary>Puts back what rules hold at once, from any thread, as the host crashes.</summary>
    public void ReleaseNow() => _ruler?.Controller.ReleaseNow();

    // A failure puts back what rules hold, as stopping does.
    protected override void OnFailed(Exception exception)
    {
        LogFailed(_logger, exception);
        try
        {
            _ruler?.Controller.ReleaseNow();
        }
        catch (Exception release)
        {
            LogReleaseFailed(_logger, release);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules applied its settings: {Count} rules, watching windows {Watching}.")]
    private static partial void LogApplied(ILogger logger, int count, bool watching);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Rules failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Rules couldn't put back the windows it changed after a failure.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules put back {Count} windows: {Why}.")]
    private static partial void LogReleased(ILogger logger, int count, string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules can't serve the settings app's pipe (error {ErrorCode}), so Apply now doesn't work.")]
    private static partial void LogPipeNotServed(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules' pipe for the settings app stopped (error {ErrorCode}).")]
    private static partial void LogPipeFailed(ILogger logger, int errorCode);

    // What runs on the feature's thread: the controller, its hooks and the pipe.
    private sealed class Ruler : IDisposable
    {
        // How long after a change what rules remember is written.
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

        private readonly WindowRulesThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsRulesSystem _system;
        private readonly List<WinEventHook> _hooks = [];
        private readonly RequestPipe? _pipe;
        private readonly uint _taskbarCreated = MessageWindow.RegisterMessage("TaskbarCreated");
        private ITimer? _save;
        private bool _unsaved;

        public Ruler(WindowRulesThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            Action<Action> post = action => window.BeginInvoke(() => owner.Guard(action));
            _system = new WindowsRulesSystem(post, owner.Guard, owner._dropDown, owner._tabs, owner._requests);
            Controller = new RulesController(_system, owner._time, post, owner._logger);
            _system.RefreshDisplays();
            _pipe = owner._pipeName is null ? null : Serve();
            _window.MessageReceived += OnMessageReceived;
            Controller.Remembered.Changed += (_, _) => ScheduleSave();
            if (owner.Keeps)
            {
                owner._readRemembered?.Invoke(owner);
            }
        }

        public RulesController Controller { get; }

        public void Apply(WindowRulesSettings settings)
        {
            Controller.Apply(settings);
            if (Controller.IsWatching)
            {
                Hook();
            }
            else
            {
                Unhook();
            }

            LogApplied(_owner._logger, Controller.Rules.Matchers.Count, Controller.IsWatching);
        }

        // Every window rules changed gets back what it had before the hooks and the window go.
        public void Dispose()
        {
            _pipe?.Dispose();
            _window.MessageReceived -= OnMessageReceived;
            Unhook();
            SaveNow();
            _save?.Dispose();
            var released = Controller.ReleaseNow();
            if (released > 0)
            {
                LogReleased(_owner._logger, released, "Window Rules stopped");
            }

            Controller.Dispose();
            _system.Dispose();
            _owner._ruler = null;
        }

        // Out-of-context hooks for every process, which run no code in other apps, set only while
        // some rule can be used.
        private void Hook()
        {
            if (_hooks.Count > 0)
            {
                return;
            }

            Add(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectHideEvent, (type, window) =>
            {
                if (type == WinEventHook.ObjectShowEvent)
                {
                    Controller.OnShown(window);
                }
                else if (type == WinEventHook.ObjectHideEvent)
                {
                    Controller.OnHidden(window);
                }
                else
                {
                    Controller.OnDestroyed(window);
                }
            });
            Add(WinEventHook.ObjectUncloakedEvent, WinEventHook.ObjectUncloakedEvent, (_, window) => Controller.OnUncloaked(window));
            Add(WinEventHook.ObjectNameChangeEvent, WinEventHook.ObjectNameChangeEvent, (_, window) => Controller.OnTitleChanged(window));
            Add(WinEventHook.MoveSizeStartEvent, WinEventHook.MoveSizeEndEvent, (type, window) =>
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
            Add(WinEventHook.MinimizeStartEvent, WinEventHook.MinimizeEndEvent, (type, window) =>
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
            Add(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, (_, window) => Controller.OnForeground(window));
        }

        private void Unhook()
        {
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }

            _hooks.Clear();
        }

        private void Add(uint first, uint last, Action<uint, nint> handle)
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
            _owner._saveRemembered?.Invoke(_owner, Controller.Remembered.Saved);
        }

        // The displays are read again after they change, for rules with a chosen display, buttons
        // Explorer adds again as it restarts are removed again, and what rules remember is written
        // when the session may end.
        private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (e.Message == QueryEndSession)
            {
                _owner.Guard(SaveNow);
                return;
            }

            if (Displays.IsChangeMessage(e.Message))
            {
                _owner.Guard(() => _system.RefreshDisplays(Controller.OnDisplaysChanged));
            }
            else if (e.Message == _taskbarCreated)
            {
                _owner.Guard(Controller.OnTaskbarCreated);
            }
        }

        // The settings app's requests come through the pipe on the thread pool and are answered on this thread.
        private RequestPipe? Serve()
        {
            try
            {
                return RequestPipe.Serve(_owner._pipeName!, WindowRulesPipe.MaximumRequestLength, AnswerAsync, exception => LogPipeFailed(_owner._logger, exception.HResult));
            }
            catch (Win32Exception exception)
            {
                LogPipeNotServed(_owner._logger, exception.NativeErrorCode);
                return null;
            }
        }

        // On the thread pool. A request that can't be read, or that the thread can't take because
        // it ended or failed, is answered with no windows.
        private Task<byte[]> AnswerAsync(byte[] request)
        {
            var none = WindowRulesPipe.Write(new WindowRulesAnswer());
            if (WindowRulesPipe.ReadRequest(request) is not { } read)
            {
                return Task.FromResult(none);
            }

            var answered = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var posted = _window.BeginInvoke(() =>
            {
                var taken = false;
                _owner.Guard(() =>
                {
                    var windows = Controller.ApplyNow(read.Rule);
                    answered.TrySetResult(WindowRulesPipe.Write(new WindowRulesAnswer { Windows = windows }));
                    taken = true;
                });

                if (!taken)
                {
                    answered.TrySetResult(none);
                }
            });

            if (!posted)
            {
                answered.TrySetResult(none);
            }

            return answered.Task;
        }
    }
}
