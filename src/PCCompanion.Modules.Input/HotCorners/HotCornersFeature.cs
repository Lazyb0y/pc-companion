using System;
using System.ComponentModel;
using System.Diagnostics;
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
/// Runs an action when the pointer reaches a corner of a display. See docs/features/input/hot-corners.md.
/// </summary>
public sealed partial class HotCornersFeature(ILogger<HotCornersFeature> logger, TimeProvider time) : IFeature
{
    private const string WindowClassName = "PCCompanion.HotCorners";

    private readonly CornerDetector _detector = new(new WindowsHotCornerEnvironment(), time);
    private readonly ProgramRunner _programs = new(logger, () => Environment.IsPrivilegedProcess, File.Exists, StartProcess, action => Task.Run(action));
    private HotCornersSettings _settings = new();
    private MessageWindow? _window;
    private RawPointerMonitor? _monitor;
    private DisplayKeeper? _displays;
    private ITimer? _timer;
    private bool _failed;

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

        try
        {
            _settings = HotCornersSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            _settings = new HotCornersSettings();
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
            _monitor = new RawPointerMonitor(window);
        }
        catch
        {
            window.Dispose();
            throw;
        }

        _window = window;
        _window.MessageReceived += OnMessageReceived;
        _monitor.InputReceived += OnInputReceived;
        _displays = new DisplayKeeper(logger, () => KeepDisplaysOn(window));

        // Fires on the thread pool; the detector runs on the window's thread.
        _timer = time.CreateTimer(_ => window.BeginInvoke(Update), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _displays?.End(KeepDisplaysOnEnd.HotCornersStopped);
        _timer?.Dispose();
        _monitor?.Dispose();
        _window.Dispose();
        (_displays, _timer, _monitor, _window) = (null, null, null, null);
        LogStopped(logger);
    }

    private void Configure()
    {
        var displays = Displays.GetAreas();
        _detector.Configure(_settings, displays);
        _displays?.Check(_detector.CurrentCorner, _settings);
        LogConfigured(logger, displays.Count, _settings.ActivationDelayMilliseconds);
    }

    private void OnInputReceived(object? sender, EventArgs e) => Update();

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (Displays.IsChangeMessage(e.Message))
        {
            Guard(Configure);
        }
    }

    private void Update() => Guard(() =>
    {
        // A timer can fire after the feature stopped.
        if (_window is not { } window)
        {
            return;
        }

        var decision = _detector.Update();
        _displays?.Check(_detector.CurrentCorner, _settings);
        switch (decision.Outcome)
        {
            case CornerOutcome.Waiting:
                _timer?.Change(decision.Wait, Timeout.InfiniteTimeSpan);
                break;
            case CornerOutcome.Triggered:
                LogTriggered(logger, decision.Corner, decision.Action);
                if (!Run(decision, window))
                {
                    LogActionRefused(logger, decision.Action);
                }

                break;
            case CornerOutcome.MouseButtonHeld or CornerOutcome.KeyNotHeld or CornerOutcome.PointerConfined or CornerOutcome.PointerHidden or CornerOutcome.FullScreenApp:
                LogSkipped(logger, decision.Corner, decision.Action, decision.Outcome);
                break;
        }
    });

    private bool Run(CornerDecision decision, MessageWindow window) => decision.Action switch
    {
        HotCornerAction.TaskView => ShellActions.ShowTaskView(),
        HotCornerAction.ShowDesktop => ShellActions.ShowDesktop(),
        HotCornerAction.Start => ShellActions.OpenStart(),
        HotCornerAction.Search => ShellActions.OpenSearch(),
        HotCornerAction.Notifications => ShellActions.OpenNotifications(),
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

    // The runner starts the program on the thread pool and logs its own problems.
    private bool RunProgram(ScreenCorner corner)
    {
        _programs.Run(_settings.For(corner));
        return true;
    }

    private static void StartProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
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
            LogFailed(logger, exception);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't run {Action}.")]
    private static partial void LogActionRefused(ILogger logger, HotCornerAction action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Hot Corners settings can't be read, so Hot Corners is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Hot Corners failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
