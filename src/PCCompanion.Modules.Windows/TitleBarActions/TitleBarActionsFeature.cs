using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Runs actions for a middle-click or the wheel on any window's title bar. See docs/features/windows/title-bar-actions.md.
/// </summary>
public sealed partial class TitleBarActionsFeature(ILogger<TitleBarActionsFeature> logger, TimeProvider time) : IFeature
{
    private const string WindowClassName = "PCCompanion.TitleBarActions";

    private TitleBarActionsSettings _settings = new();
    private MessageWindow? _window;
    private RawPointerMonitor? _monitor;
    private PointerHook? _hook;
    private TitleBarController? _controller;
    private bool _failed;
    private bool _recovered;

    public static FeatureInfo Definition { get; } = new("titleBarActions", WindowsModule.Id, "TitleBarActionsName", "TitleBarActionsDescription", "");

    /// <summary>
    /// Whether this PC can run Title Bar Actions, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _window is not null;

    public void Apply(JsonElement? settings)
    {
        // Windows left translucent by a PC Companion that ended without putting them back get their
        // opacity back, on or off; it reads every window's properties without waiting for any program.
        if (!_recovered)
        {
            _recovered = true;
            var recovered = WindowOpacity.Recover(new WindowsWindowOpacitySystem(), Background, exception => LogOpacityFailed(logger, exception));
            if (recovered > 0)
            {
                LogRecovered(logger, recovered);
            }
        }

        if (_failed)
        {
            return;
        }

        try
        {
            _settings = TitleBarActionsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            _settings = new TitleBarActionsSettings();
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
        RawPointerMonitor? monitor = null;
        try
        {
            monitor = new RawPointerMonitor();
            _hook = new PointerHook(window);
        }
        catch
        {
            monitor?.Dispose();
            window.Dispose();
            throw;
        }

        _window = window;
        _monitor = monitor;

        // The controller's timer and opacity changes run on the thread pool; the controller runs on
        // the window's thread.
        void Post(Action action) => window.BeginInvoke(() => Guard(action));
        var opacity = new WindowOpacity(new WindowsWindowOpacitySystem(), Background, Post, exception => LogOpacityFailed(logger, exception));
        var controller = new TitleBarController(new WindowsTitleBarWindows(), new WindowsTitleBarActionsSystem(_hook), opacity, time, Post, logger);
        _controller = controller;
        _monitor.InputReceived += (_, input) => Guard(() => controller.OnPointerInput(input));
        _hook.Kept += (_, input) => Guard(() => controller.OnKept(input));
        _hook.Failed += (_, failure) => Guard(() => throw new InvalidOperationException("The pointer hook failed.", failure.Exception));

        // Setup closing PC Companion for an update or a removal, Restart Manager and the end of the
        // session put the windows back, as Exit does.
        window.MessageReceived += (_, message) =>
        {
            if (MessageWindow.IsCloseRequest(message.Message, message.WParam))
            {
                Guard(() =>
                {
                    var putBack = controller.PutBack();
                    LogPutBack(logger, putBack);
                });
            }
        };

        // A crash ends the host without stopping the feature, so the windows get their opacity back first.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        var putBack = _controller?.Stop() ?? 0;
        _controller?.Dispose();
        _hook?.Dispose();
        _monitor?.Dispose();
        _window.Dispose();
        (_controller, _hook, _monitor, _window) = (null, null, null, null);
        LogPutBack(logger, putBack);
        LogStopped(logger);
    }

    // Giving a window the layered style waits for its program, so opacity changes run here.
    private static void Background(Action action) => Task.Run(action);

    // Runs on the crashing thread; nothing here may throw.
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _controller?.PutBackNow();
        }
        catch (Exception exception)
        {
            LogOpacityFailed(logger, exception);
        }
    }

    private void Configure()
    {
        foreach (var action in new[] { _settings.MiddleClick, _settings.ShiftMiddleClick, _settings.ControlMiddleClick })
        {
            if (!Enum.IsDefined(action))
            {
                LogUnknownAction(logger);
            }
        }

        foreach (var action in new[] { _settings.Wheel, _settings.ControlWheel })
        {
            if (!Enum.IsDefined(action))
            {
                LogUnknownAction(logger);
            }
        }

        _controller?.Configure(_settings);
        LogConfigured(logger, _settings.MiddleClick, _settings.ShiftMiddleClick, _settings.ControlMiddleClick, _settings.Wheel, _settings.ControlWheel);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions applied its settings: middle-click {MiddleClick}, Shift + middle-click {ShiftMiddleClick}, Ctrl + middle-click {ControlMiddleClick}, wheel {Wheel}, Ctrl + wheel {ControlWheel}.")]
    private static partial void LogConfigured(ILogger logger, TitleBarClickAction middleClick, TitleBarClickAction shiftMiddleClick, TitleBarClickAction controlMiddleClick, TitleBarWheelAction wheel, TitleBarWheelAction controlWheel);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions gave {Count} windows their opacity back.")]
    private static partial void LogPutBack(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Title Bar Actions gave {Count} windows left translucent their opacity back.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Title Bar Actions couldn't change a window's opacity.")]
    private static partial void LogOpacityFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Title Bar Actions action in the settings is unknown, so it does nothing.")]
    private static partial void LogUnknownAction(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Title Bar Actions settings can't be read, so Title Bar Actions is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Title Bar Actions failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
