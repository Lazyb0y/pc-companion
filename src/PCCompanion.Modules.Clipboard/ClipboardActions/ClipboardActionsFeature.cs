using System;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// Shows actions for the copied text with Windows + Alt + V, and for the selected text with
/// Windows + Alt + S. See docs/features/clipboard/clipboard-actions.md.
/// </summary>
public sealed partial class ClipboardActionsFeature(IClipboardActionsSystem system, IHostCommands commands, ILogger<ClipboardActionsFeature> logger) : IFeature
{
    // How long the host waits as it exits for the thread to remove the shortcuts.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly ClipboardShortcutStatus _status = new();
    private ClipboardActionsThread? _thread;
    private ClipboardActionsThread? _stopped;
    private SnapshotPipe? _statusPipe;
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
            _thread = new ClipboardActionsThread(read, system, commands, logger, _stopped, _status);
            _stopped = null;
            _statusPipe = ServeStatus();
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

    // Doesn't wait: a new thread waits for this one before it registers the shortcuts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
        _statusPipe?.Dispose();
        _statusPipe = null;
        _status.Refused = new ClipboardShortcutRefusals(false, false);
    }

    // The settings app shows which shortcuts Windows refused; without the pipe, the shortcuts still work.
    private SnapshotPipe? ServeStatus()
    {
        try
        {
            return SnapshotPipe.Serve(ClipboardShortcutStatus.PipeName, _status.ToJson, exception => LogStatusFailed(logger, exception));
        }
        catch (Win32Exception exception)
        {
            LogStatusFailed(logger, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Clipboard Actions settings can't be read, so Clipboard Actions is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions can't tell the settings app which shortcuts Windows refused.")]
    private static partial void LogStatusFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Clipboard Actions didn't stop within {Seconds} s, for example while it waited for the app that copied the text.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
