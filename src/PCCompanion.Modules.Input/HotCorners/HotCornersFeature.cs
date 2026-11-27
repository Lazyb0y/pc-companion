using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Hot Corners &amp; Edges: runs an action when the pointer reaches a corner or pushes against an
/// edge of a display, and another when the wheel turns there. See docs/features/input/hot-corners.md.
/// </summary>
public sealed partial class HotCornersFeature : IFeature
{
    private const string WindowClassName = "PCCompanion.HotCorners";

    // How long after raw input reports a wheel notch the hook has surely seen it.
    private static readonly TimeSpan WheelCheckDelay = TimeSpan.FromMilliseconds(100);

    // Raw input can report a move before the pointer's position follows it, and once the pointer
    // stops no event comes to look again, so the position is looked at once more this long after
    // the last move, as Title Bar Actions does.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(50);

    private readonly ILogger<HotCornersFeature> _logger;
    private readonly TimeProvider _time;
    private readonly WindowsHotCornerEnvironment _environment = new();
    private readonly CornerDetector _detector;
    private readonly WheelSteps _wheelSteps;
    private readonly ProgramRunner _programs;
    private readonly CornerResults _results;
    private HotCornersSettings _settings = new();
    private MessageWindow? _window;
    private RawPointerMonitor? _monitor;
    private PointerHook? _hook;
    private WheelSpot? _claimed;
    private ScreenCorner? _reached;
    private uint? _wheelToCheck;
    private ITimer? _wheelCheck;
    private ITimer? _settle;
    private bool _settleIsPosition;
    private SnapshotPipe? _resultsPipe;
    private DisplayKeeper? _displays;
    private ITimer? _timer;
    private bool _timerSet;
    private bool _failed;

    public HotCornersFeature(ILogger<HotCornersFeature> logger, TimeProvider time, IHostCommands commands)
    {
        _logger = logger;
        _time = time;
        _detector = new CornerDetector(_environment, time);
        _wheelSteps = new WheelSteps(time);
        _programs = new ProgramRunner(_logger, File.Exists, commands, action => Task.Run(action));
        _results = new CornerResults(time);
    }

    public static FeatureInfo Definition { get; } = new("hotCorners", InputModule.Id, "HotCornersName", "HotCornersDescription", "");

    /// <summary>
    /// Whether this PC can run Hot Corners, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _window is not null;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        HotCornersSettings read;
        try
        {
            read = HotCornersSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            read = new HotCornersSettings();
        }

        // Saving other settings, such as another feature's, leaves a running Hot Corners as it is,
        // so a corner the pointer is in isn't reset.
        var unchanged = _window is not null && read == _settings;
        _settings = read;
        if (unchanged)
        {
            return;
        }

        if (!_settings.Enabled)
        {
            Stop();
            return;
        }

        if (_window is null)
        {
            Start();
        }

        Configure();
    }

    public void Dispose() => Stop();

    private void Start()
    {
        var window = new MessageWindow(WindowClassName);
        try
        {
            _monitor = new RawPointerMonitor();
        }
        catch
        {
            window.Dispose();
            throw;
        }

        _window = window;
        _window.MessageReceived += OnMessageReceived;
        _monitor.InputReceived += OnInputReceived;
        _displays = new DisplayKeeper(_logger, () => KeepDisplaysOn(window));
        _hook = new PointerHook(window);
        _hook.Kept += (_, input) => Guard(() => OnWheelKept(input));
        _hook.Failed += (_, e) => Guard(() => OnHookFailed(e.Exception));

        // Fire on the thread pool; the detector runs on the window's thread.
        _timer = _time.CreateTimer(_ => window.BeginInvoke(() => Update(Size.Empty, isPosition: false)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _wheelCheck = _time.CreateTimer(_ => window.BeginInvoke(() => Guard(CheckWheel)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _settle = _time.CreateTimer(_ => window.BeginInvoke(Settle), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _resultsPipe = ServeResults();
        LogStarted(_logger);
    }

    // The settings app shows the results while Hot Corners runs; without the pipe, corners still work.
    private SnapshotPipe? ServeResults()
    {
        try
        {
            return SnapshotPipe.Serve(CornerResults.PipeName, _results.ToJson, exception => LogResultsFailed(_logger, exception));
        }
        catch (Win32Exception exception)
        {
            LogResultsFailed(_logger, exception);
            return null;
        }
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _displays?.End(KeepDisplaysOnEnd.HotCornersStopped);
        _timer?.Dispose();
        _wheelCheck?.Dispose();
        _settle?.Dispose();
        _hook?.Dispose();
        _monitor?.Dispose();
        _resultsPipe?.Dispose();
        _window.Dispose();
        (_displays, _timer, _wheelCheck, _settle, _hook, _monitor, _resultsPipe, _window) = (null, null, null, null, null, null, null, null);
        (_claimed, _reached, _wheelToCheck) = (null, null, null);
        _timerSet = false;
        _results.Clear();
        LogStopped(_logger);
    }

    private void Configure()
    {
        var displays = Displays.GetDetails();
        _detector.Configure(_settings, displays);

        // Configuring ends any wait: a corner or edge the pointer is on counts as decided, and its
        // wheel waits until the pointer comes back.
        CancelTimer();
        _reached = _detector.CurrentCorner;
        ClaimWheel();
        _displays?.Check(_detector.CurrentCorner, _settings);
        LogConfigured(_logger, displays.Count, _settings.ActivationDelayMilliseconds);
    }

    private void OnInputReceived(object? sender, PointerInputEventArgs e)
    {
        // A wheel notch the hook never saw while it was set means Windows removed the hook.
        if ((e.Changes & PointerButtonChanges.Wheel) != 0 && _claimed is not null && _wheelToCheck is null)
        {
            _wheelToCheck = e.Time;
            _wheelCheck?.Change(WheelCheckDelay, Timeout.InfiniteTimeSpan);
        }

        Update(e.Movement, e.IsPosition);
        if (e.Movement != Size.Empty || e.IsPosition)
        {
            _settleIsPosition = e.IsPosition;
            _settle?.Change(SettleDelay, Timeout.InfiniteTimeSpan);
        }
    }

    // A pointer that stopped where the last event didn't see it yet, such as on an edge it had just
    // reached, is looked at once more, as that event: a position from a pen or a remote session
    // must still make the edge wait for the delay rather than a push.
    private void Settle()
    {
        if (_window is not null && InputState.PointerPosition != _detector.LastPosition)
        {
            Update(Size.Empty, _settleIsPosition);
        }
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (Displays.IsChangeMessage(e.Message))
        {
            Guard(Configure);
        }
    }

    private void Update(Size movement, bool isPosition) => Guard(() =>
    {
        // A timer can fire after the feature stopped.
        if (_window is not { } window)
        {
            return;
        }

        var decision = _detector.Update(movement, isPosition);
        Reached();
        ClaimWheel();
        _displays?.Check(_detector.CurrentCorner, _settings);

        // Once the pointer has left the corner or the corner was decided, the timer has nothing to wait for.
        if (decision.Outcome != CornerOutcome.Waiting)
        {
            CancelTimer();
        }

        switch (decision.Outcome)
        {
            case CornerOutcome.Waiting:
                _timer?.Change(decision.Wait, Timeout.InfiniteTimeSpan);
                _timerSet = true;
                break;
            case CornerOutcome.Triggered:
                LogRan(decision);
                var ran = Run(decision, window);
                if (!ran)
                {
                    LogActionRefused(_logger, decision.Action);
                }

                // Run a program adds its own result once the program has started or not.
                if (decision.Action != HotCornerAction.RunProgram)
                {
                    _results.Add(decision.Corner, decision.Action, ran ? CornerResultKind.Ran : CornerResultKind.Refused);
                }

                break;
            case CornerOutcome.LeftEarly or CornerOutcome.MouseButtonHeld or CornerOutcome.KeyNotHeld or CornerOutcome.PointerConfined or CornerOutcome.PointerHidden or CornerOutcome.FullScreenApp or CornerOutcome.AppExcluded or CornerOutcome.TaskbarHides:
                LogNotRun(decision);
                _results.Add(decision.Corner, decision.Action, CornerResults.KindOf(decision.Outcome));
                break;
        }
    });

    private void LogRan(CornerDecision decision)
    {
        if (CornerDetector.IsEdge(decision.Corner))
        {
            LogEdgeRan(_logger, decision.Corner, decision.Action);
        }
        else
        {
            LogTriggered(_logger, decision.Corner, decision.Action);
        }
    }

    private void LogNotRun(CornerDecision decision)
    {
        if (CornerDetector.IsEdge(decision.Corner))
        {
            LogEdgeSkipped(_logger, decision.Corner, decision.Action, decision.Outcome);
        }
        else
        {
            LogSkipped(_logger, decision.Corner, decision.Action, decision.Outcome);
        }
    }

    // Once as the pointer reaches an edge with a push action, Explorer is asked on the thread pool
    // whether a bar hides itself there, so the answer is ready, or nearly, when the push is enough.
    private void Reached()
    {
        var place = _detector.CurrentCorner;
        if (place == _reached)
        {
            return;
        }

        _reached = place;
        if (place is { } edge && CornerDetector.IsEdge(edge) && _settings.For(edge).Action != HotCornerAction.None && _detector.CurrentDisplay is { } display)
        {
            _environment.AskAutoHideBar(display, CornerDetector.SideOf(edge));
        }
    }

    // The wheel is claimed only while the pointer is on a corner or edge whose wheel action applies,
    // ahead of other features' claims there, and never while an app has captured the pointer.
    private void ClaimWheel()
    {
        var wheel = _detector.Wheel;
        if (wheel == _claimed)
        {
            return;
        }

        _claimed = wheel;
        _wheelSteps.Reset();
        _hook?.Claim(wheel is { } spot
            ? new PointerClaim(spot.Area, new HashSet<PointerTrigger> { new(PointerButton.Wheel, KeysOf(spot.Key)) }) { NotWhileCaptured = true, First = true }
            : null);
    }

    private static PointerKeys KeysOf(HotCornerKey key) => key switch
    {
        HotCornerKey.Control => PointerKeys.Control,
        HotCornerKey.Alt => PointerKeys.Alt,
        HotCornerKey.Shift => PointerKeys.Shift,
        HotCornerKey.Windows => PointerKeys.Windows,
        _ => PointerKeys.None,
    };

    private void OnWheelKept(KeptPointerInputEventArgs input)
    {
        // The claim can have gone while the notch was on its way.
        if (_claimed is not { } spot)
        {
            return;
        }

        var steps = _wheelSteps.Add(spot.Action, input.WheelDelta);
        var keepKeys = spot.Key != HotCornerKey.None;
        for (var step = 0; step < Math.Abs(steps); step++)
        {
            var up = steps > 0;
            var ran = spot.Action switch
            {
                HotCornerWheelAction.Volume => ShellActions.ChangeVolume(louder: up, keepKeys),
                HotCornerWheelAction.Desktops => ShellActions.SwitchDesktop(next: !up, keepKeys),
                HotCornerWheelAction.Tracks => ShellActions.SwitchTrack(next: !up, keepKeys),
                _ => true,
            };
            LogWheel(_logger, spot.Position, spot.Action, up ? "up" : "down");
            if (!ran)
            {
                LogWheelRefused(_logger, spot.Action);
            }
        }

        // Releasing the Windows key after a turn that sent nothing would open Start.
        if (steps == 0 && spot.Key == HotCornerKey.Windows)
        {
            ShellActions.PressMaskKey();
        }
    }

    // A notch on a claimed corner or edge that the hook never saw while it was set means Windows
    // removed the hook, as it does with one it finds slow.
    private void CheckWheel()
    {
        if (_wheelToCheck is not { } notch)
        {
            return;
        }

        _wheelToCheck = null;
        if (_claimed is not null && PointerHook.Outcome(notch) == PointerInputOutcome.NotSeen)
        {
            LogHookRemoved(_logger);
            PointerHook.SetAgain();
        }
    }

    // The hook drops every claim when its thread fails; the next pointer event claims again.
    private void OnHookFailed(Exception exception)
    {
        LogHookFailed(_logger, exception);
        _claimed = null;
    }

    // Only while it's set: most pointer events come while no corner waits.
    private void CancelTimer()
    {
        if (_timerSet)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timerSet = false;
        }
    }

    private bool Run(CornerDecision decision, MessageWindow window) => decision.Action switch
    {
        HotCornerAction.TaskView => ShellActions.ShowTaskView(),
        HotCornerAction.ShowDesktop => ShellActions.ShowDesktop(),
        HotCornerAction.Start => ShellActions.OpenStart(),
        HotCornerAction.Search => ShellActions.OpenSearch(),
        HotCornerAction.Notifications => ShellActions.OpenNotifications(),
        HotCornerAction.PreviousDesktop => ShellActions.SwitchDesktop(next: false),
        HotCornerAction.NextDesktop => ShellActions.SwitchDesktop(next: true),
        HotCornerAction.Lock => ShellActions.Lock(),
        HotCornerAction.TurnOffDisplays => ShellActions.TurnOffDisplays(window),
        HotCornerAction.ScreenSaver => ShellActions.StartScreenSaver(window),
        HotCornerAction.RunProgram => RunProgram(decision.Corner),
        HotCornerAction.KeepDisplaysOn => _displays?.Start(decision.Corner) ?? false,
        _ => true,
    };

    // Watches for the session lock first: on the lock screen the displays turn off as usual.
    private DisplaysKeptOn? KeepDisplaysOn(MessageWindow window)
    {
        SessionMonitor sessions;
        try
        {
            sessions = new SessionMonitor(window);
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (DisplayRequest.Start(Strings.HotCornersKeepDisplaysOnReason) is not { } request)
        {
            sessions.Dispose();
            return null;
        }

        sessions.Locked += (_, _) => Guard(() => _displays?.End(KeepDisplaysOnEnd.SessionLocked));
        return new DisplaysKeptOn(request, sessions);
    }

    // The runner starts the program on the thread pool, logs its own problems and adds the result.
    private bool RunProgram(ScreenCorner corner)
    {
        _programs.Run(_settings.For(corner), kind => _results.Add(corner, HotCornerAction.RunProgram, kind));
        return true;
    }

    // Runs on the window's thread, where an exception would end the host. Like a failing Apply,
    // a failure stops the feature until the host restarts.
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogFailed(_logger, exception);
            _failed = true;
            Stop();
        }
    }

    private sealed class DisplaysKeptOn(DisplayRequest request, SessionMonitor sessions) : IDisposable
    {
        public void Dispose()
        {
            request.Dispose();
            sessions.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hot Corners started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hot Corners stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hot Corners applied its settings: displays {DisplayCount}, delay {DelayMilliseconds} ms.")]
    private static partial void LogConfigured(ILogger logger, int displayCount, int delayMilliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Corner} corner ran {Action}.")]
    private static partial void LogTriggered(ILogger logger, ScreenCorner corner, HotCornerAction action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Corner} corner didn't run {Action}: {Reason}.")]
    private static partial void LogSkipped(ILogger logger, ScreenCorner corner, HotCornerAction action, CornerOutcome reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Edge} edge ran {Action}.")]
    private static partial void LogEdgeRan(ILogger logger, ScreenCorner edge, HotCornerAction action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {Edge} edge didn't run {Action}: {Reason}.")]
    private static partial void LogEdgeSkipped(ILogger logger, ScreenCorner edge, HotCornerAction action, CornerOutcome reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The wheel on {Place} ran {Action}, turned {Direction}.")]
    private static partial void LogWheel(ILogger logger, ScreenCorner place, HotCornerWheelAction action, string direction);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't run the wheel's {Action}.")]
    private static partial void LogWheelRefused(ILogger logger, HotCornerWheelAction action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows removed the pointer hook, so Hot Corners & Edges set it again.")]
    private static partial void LogHookRemoved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The pointer hook failed, so the wheel reaches the apps until the pointer comes back to a corner or edge.")]
    private static partial void LogHookFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't run {Action}.")]
    private static partial void LogActionRefused(ILogger logger, HotCornerAction action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hot Corners can't give the settings app its recent results.")]
    private static partial void LogResultsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Hot Corners settings can't be read, so Hot Corners is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hot Corners failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
