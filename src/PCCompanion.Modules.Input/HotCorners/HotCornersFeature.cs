using System;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Runs an action when the pointer reaches a corner of a display. See docs/features/input/hot-corners.md.
/// </summary>
public sealed partial class HotCornersFeature(ILogger<HotCornersFeature> logger, TimeProvider time) : IFeature
{
    private const string WindowClassName = "PCCompanion.HotCorners";

    private readonly CornerDetector _detector = new(new WindowsHotCornerEnvironment(), time);
    private HotCornersSettings _settings = new();
    private MessageWindow? _window;
    private RawPointerMonitor? _monitor;
    private ITimer? _timer;
    private bool _failed;

    public static FeatureInfo Definition { get; } = new("hotCorners", InputModule.Id, "HotCornersName", "HotCornersDescription", "");

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => FeatureSupport.Supported;

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

        _timer?.Dispose();
        _monitor?.Dispose();
        _window.Dispose();
        (_timer, _monitor, _window) = (null, null, null);
        LogStopped(logger);
    }

    private void Configure()
    {
        var displays = Displays.GetAreas();
        _detector.Configure(_settings, displays);
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
        switch (decision.Outcome)
        {
            case CornerOutcome.Waiting:
                _timer?.Change(decision.Wait, Timeout.InfiniteTimeSpan);
                break;
            case CornerOutcome.Triggered:
                LogTriggered(logger, decision.Corner, decision.Action);
                if (!Run(decision.Action, window))
                {
                    LogActionRefused(logger, decision.Action);
                }

                break;
            case CornerOutcome.MouseButtonHeld or CornerOutcome.KeyNotHeld or CornerOutcome.FullScreenApp:
                LogSkipped(logger, decision.Corner, decision.Action, decision.Outcome);
                break;
        }
    });

    private static bool Run(HotCornerAction action, MessageWindow window) => action switch
    {
        HotCornerAction.TaskView => ShellActions.ShowTaskView(),
        HotCornerAction.ShowDesktop => ShellActions.ShowDesktop(),
        HotCornerAction.Start => ShellActions.OpenStart(),
        HotCornerAction.Search => ShellActions.OpenSearch(),
        HotCornerAction.Notifications => ShellActions.OpenNotifications(),
        HotCornerAction.Lock => ShellActions.Lock(),
        HotCornerAction.TurnOffDisplays => ShellActions.TurnOffDisplays(window),
        HotCornerAction.ScreenSaver => ShellActions.StartScreenSaver(window),
        _ => true,
    };

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hot Corners started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hot Corners stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hot Corners uses {DisplayCount} displays and a {DelayMilliseconds} ms delay.")]
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
