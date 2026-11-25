using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Rules that act on an app's windows as they open: their display, place and size, how they open,
/// and keeping them on top, translucent, off the taskbar or in a tab group. See
/// docs/features/windows/window-rules.md.
/// </summary>
public sealed partial class WindowRulesFeature : IFeature
{
    // How long the host waits as it exits for the feature's thread.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time;
    private readonly ILogger<WindowRulesFeature> _logger;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly string? _pipeName;
    private WindowRulesThread? _thread;
    private WindowRulesThread? _stopped;
    private bool _failed;

    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    public WindowRulesFeature(TimeProvider time, ILogger<WindowRulesFeature> logger, DropDownWindows dropDown, WindowTabsWindows tabs)
        : this(time, logger, dropDown, tabs, WindowRulesPipe.Name)
    {
    }

    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    /// <param name="pipeName">The pipe the settings app asks through for Apply now; none in tests.</param>
    public WindowRulesFeature(TimeProvider time, ILogger<WindowRulesFeature> logger, DropDownWindows dropDown, WindowTabsWindows tabs, string? pipeName)
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
    }

    public static FeatureInfo Definition { get; } = new("windowRules", WindowsModule.Id, "WindowRulesName", "WindowRulesDescription", "\uE8A1");

    /// <summary>
    /// Whether this PC can run Window Rules, for the settings app, which doesn't create the feature.
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

        WindowRulesSettings read;
        try
        {
            read = WindowRulesSettings.Read(settings);
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
            _thread = new WindowRulesThread(read, _time, _logger, _dropDown, _tabs, _stopped, _pipeName);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Rules settings can't be read, so Window Rules is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
