using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Shows actions for the copied text with Windows + Alt + V. See docs/features/clipboard/clipboard-actions.md.
/// </summary>
public sealed partial class ClipboardActionsFeature(IClipboardActionsSystem system, IHostCommands commands, ILogger<ClipboardActionsFeature> logger) : IFeature
{
    // How long the host waits as it exits for the thread to remove the shortcut.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private ClipboardActionsThread? _thread;
    private ClipboardActionsThread? _stopped;
    private bool _failed;

    public ClipboardActionsFeature(IHostCommands commands, ILogger<ClipboardActionsFeature> logger)
        : this(new WindowsClipboardActionsSystem(), commands, logger)
    {
    }

    public static FeatureInfo Definition { get; } = new("clipboardActions", ClipboardModule.Id, "ClipboardActionsName", "ClipboardActionsDescription", "");

    /// <summary>
    /// Whether this PC can run Clipboard Actions, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public void Apply(JsonElement? settings)
    {
        // A failure on the feature's thread keeps the feature off, like a failing Apply.
        if (_thread is { HasFailed: true })
        {
            _failed = true;
            Stop();
        }

        if (_failed)
        {
            return;
        }

        ClipboardActionsSettings read;
        try
        {
            read = ClipboardActionsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        if (_thread is null)
        {
            _thread = new ClipboardActionsThread(read, system, commands, logger, _stopped);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    // Doesn't wait: a new thread waits for this one before it registers the shortcut.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Clipboard Actions settings can't be read, so Clipboard Actions is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions didn't stop within {Seconds} s, for example while it waited for the app that copied the text.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
