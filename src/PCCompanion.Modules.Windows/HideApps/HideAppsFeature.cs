using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Minimizes every window of the app in front, or of every other app, with one shortcut, and
/// brings them back as they were. See docs/features/windows/hide-apps.md.
/// </summary>
public sealed partial class HideAppsFeature : IFeature, IHostMenuItems
{
    // How long the host waits as it exits for the feature's thread to end.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private const int ShowAllItem = 1;

    private readonly TimeProvider _time;
    private readonly ILogger<HideAppsFeature> _logger;
    private readonly HideAppsMinimized _minimized;
    private HideAppsThread? _thread;
    private HideAppsThread? _stopped;
    private bool _failed;

    /// <param name="minimized">The windows Hide Apps minimized, shared with Minimize to Tray.</param>
    public HideAppsFeature(TimeProvider time, ILogger<HideAppsFeature> logger, HideAppsMinimized minimized)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(minimized);

        _time = time;
        _logger = logger;
        _minimized = minimized;
    }

    public static FeatureInfo Definition { get; } = new("hideApps", WindowsModule.Id, "HideAppsName", "HideAppsDescription", "");

    /// <summary>
    /// Whether this PC can run Hide Apps, for the settings app, which doesn't create the feature.
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

        HideAppsSettings read;
        try
        {
            read = HideAppsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
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
            _thread = new HideAppsThread(read, _time, _logger, _minimized, _stopped);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems() =>
        _thread is { Count: > 0 } ? [new HostMenuItem(ShowAllItem, Strings.HideAppsShowAll)] : [];

    public void ChooseMenuItem(int id)
    {
        if (id == ShowAllItem)
        {
            _thread?.ShowAll();
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Hide Apps settings can't be read, so Hide Apps is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hide Apps didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
