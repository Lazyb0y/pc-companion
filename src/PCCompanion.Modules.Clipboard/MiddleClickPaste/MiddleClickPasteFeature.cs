using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>
/// Pastes the text selected in the window in front with a middle-click on a text box, at the
/// pointer, without the clipboard. See docs/features/clipboard/middle-click-paste.md.
/// </summary>
public sealed partial class MiddleClickPasteFeature(ILogger<MiddleClickPasteFeature> logger, TimeProvider time) : IFeature
{
    private const string WindowClassName = "PCCompanion.MiddleClickPaste";

    private MiddleClickPasteSettings _settings = new();
    private MessageWindow? _window;
    private RawPointerMonitor? _monitor;
    private PointerHook? _hook;
    private SelectedTextReader? _reader;
    private MiddleClickPasteController? _controller;
    private bool _failed;

    public static FeatureInfo Definition { get; } = new("middleClickPaste", ClipboardModule.Id, "MiddleClickPasteName", "MiddleClickPasteDescription", "");

    /// <summary>
    /// Whether this PC can run Middle-Click Paste, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _window is not null;

    /// <summary>The text box claimed now, for tests.</summary>
    public ClaimedBox? Claimed => _controller?.Claimed;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            _settings = MiddleClickPasteSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            _settings = new MiddleClickPasteSettings();
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

        _controller?.Configure(_settings);
        LogConfigured(logger, _settings.ExcludedApps.Count);
    }

    public void Dispose() => Stop();

    private void Start()
    {
        var window = new MessageWindow(WindowClassName);
        RawPointerMonitor? monitor = null;
        PointerHook? hook = null;
        try
        {
            monitor = new RawPointerMonitor();
            hook = new PointerHook(window);
        }
        catch
        {
            hook?.Dispose();
            monitor?.Dispose();
            window.Dispose();
            throw;
        }

        _window = window;
        _monitor = monitor;
        _hook = hook;
        _reader = new SelectedTextReader();

        // The controller's timer and the reader's answers come back to the window's thread.
        void Post(Action action) => window.BeginInvoke(() => Guard(action));
        var controller = new MiddleClickPasteController(new WindowsMiddleClickPasteSystem(hook, _reader), time, Post, logger);
        _controller = controller;
        monitor.InputReceived += (_, input) => Guard(() => controller.OnPointerInput(input));
        hook.Failed += (_, failure) => Guard(() => throw new InvalidOperationException("The pointer hook failed.", failure.Exception));
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _controller?.Dispose();
        _hook?.Dispose();
        _monitor?.Dispose();
        _reader?.Dispose();
        _window.Dispose();
        (_controller, _hook, _monitor, _reader, _window) = (null, null, null, null, null);
        LogStopped(logger);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste applied its settings: {Apps} apps keep their middle-click.")]
    private static partial void LogConfigured(ILogger logger, int apps);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Middle-Click Paste settings can't be read, so Middle-Click Paste is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Middle-Click Paste failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
