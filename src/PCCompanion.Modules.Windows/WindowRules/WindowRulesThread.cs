using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly string? _pipeName;

    // Set on the feature's thread while its message loop runs; read on others.
    private volatile Ruler? _ruler;

    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    /// <param name="pipeName">The pipe the settings app asks through for Apply now; none in tests.</param>
    public WindowRulesThread(
        WindowRulesSettings settings,
        TimeProvider time,
        ILogger logger,
        DropDownWindows dropDown,
        WindowTabsWindows tabs,
        WindowRulesThread? previous = null,
        string? pipeName = null)
        : base("Window Rules", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(tabs);

        _time = time;
        _logger = logger;
        _dropDown = dropDown;
        _tabs = tabs;
        _pipeName = pipeName;
        Start();
    }

    protected override IDisposable Open(MessageWindow window)
    {
        var ruler = new Ruler(this, window);
        _ruler = ruler;
        return ruler;
    }

    protected override void Apply(WindowRulesSettings settings) => _ruler!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules applied its settings: {Count} rules, watching windows {Watching}.")]
    private static partial void LogApplied(ILogger logger, int count, bool watching);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Rules failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules can't serve the settings app's pipe (error {ErrorCode}), so Apply now doesn't work.")]
    private static partial void LogPipeNotServed(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules' pipe for the settings app stopped (error {ErrorCode}).")]
    private static partial void LogPipeFailed(ILogger logger, int errorCode);

    // What runs on the feature's thread: the controller, its hooks and the pipe.
    private sealed class Ruler : IDisposable
    {
        private readonly WindowRulesThread _owner;
        private readonly MessageWindow _window;
        private readonly WindowsRulesSystem _system;
        private readonly List<WinEventHook> _hooks = [];
        private readonly RequestPipe? _pipe;

        public Ruler(WindowRulesThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            Action<Action> post = action => window.BeginInvoke(() => owner.Guard(action));
            _system = new WindowsRulesSystem(post, owner.Guard, owner._dropDown, owner._tabs);
            Controller = new RulesController(_system, owner._time, post, owner._logger);
            _system.RefreshDisplays();
            _pipe = owner._pipeName is null ? null : Serve();
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

        public void Dispose()
        {
            _pipe?.Dispose();
            Unhook();
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

            Add(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectShowEvent, (type, window) =>
            {
                if (type == WinEventHook.ObjectShowEvent)
                {
                    Controller.OnShown(window);
                }
                else if (type == WinEventHook.ObjectDestroyEvent)
                {
                    Controller.OnDestroyed(window);
                }
            });
            Add(WinEventHook.ObjectUncloakedEvent, WinEventHook.ObjectUncloakedEvent, (_, window) => Controller.OnUncloaked(window));
            Add(WinEventHook.ObjectNameChangeEvent, WinEventHook.ObjectNameChangeEvent, (_, window) => Controller.OnTitleChanged(window));
            Add(WinEventHook.MoveSizeStartEvent, WinEventHook.MoveSizeStartEvent, (_, window) => Controller.OnMoveSizeStart(window));
            Add(WinEventHook.MinimizeStartEvent, WinEventHook.MinimizeStartEvent, (_, window) => Controller.OnMinimizeStart(window));
            _window.MessageReceived += OnMessageReceived;
        }

        private void Unhook()
        {
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }

            if (_hooks.Count > 0)
            {
                _window.MessageReceived -= OnMessageReceived;
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

        // The displays are read again after they change, for rules with a chosen display.
        private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (Displays.IsChangeMessage(e.Message))
            {
                _owner.Guard(() => _system.RefreshDisplays());
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
