using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Shows how busy the processor, memory and network are. See docs/features/desktop/system-meters.md.
/// </summary>
public sealed partial class SystemMetersFeature(IMetersSystem system, IHostCommands commands, TimeProvider time, ILogger<SystemMetersFeature> logger) : IFeature
{
    // How long the host waits as it exits for the meters to remove their icons.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private MetersThread? _thread;
    private MetersThread? _stopped;
    private bool _failed;

    public SystemMetersFeature(IHostCommands commands, TimeProvider time, ILogger<SystemMetersFeature> logger)
        : this(new WindowsMetersSystem(), commands, time, logger)
    {
    }

    public static FeatureInfo Definition { get; } = new("systemMeters", DesktopModule.Id, "SystemMetersName", "SystemMetersDescription", "");

    /// <summary>
    /// Whether this PC can run System Meters, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public void Apply(JsonElement? settings)
    {
        // A failure on the meters thread keeps the feature off, like a failing Apply.
        if (_thread is { HasFailed: true })
        {
            _failed = true;
            Stop();
        }

        if (_failed)
        {
            return;
        }

        SystemMetersSettings read;
        try
        {
            read = SystemMetersSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        foreach (var name in SystemMetersSettings.UnknownChoices(settings))
        {
            LogUnknownChoice(logger, name);
        }

        if (!read.Enabled || !read.AnyMeter)
        {
            Stop();
            return;
        }

        if (_thread is null)
        {
            _thread = new MetersThread(read, system, commands, time, logger, _stopped);
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

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The System Meters settings can't be read, so System Meters is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The System Meters setting {Name} has a choice this version doesn't know, so it uses the default.")]
    private static partial void LogUnknownChoice(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "System Meters didn't stop within {Seconds} s, so its icons may stay on the taskbar until the pointer passes over them.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
