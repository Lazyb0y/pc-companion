using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// Rolls windows up to their title bars and down again. See docs/features/windows/window-shading.md.
/// </summary>
public sealed partial class WindowShadingFeature : IFeature, IFeatureActions, IHostMenuItems
{
    /// <summary>Rolls up the window in front.</summary>
    public const string RollUpFrontAction = "rollUp";

    /// <summary>Rolls down the window rolled up last.</summary>
    public const string RollDownLastAction = "rollDownLast";

    /// <summary>Rolls every rolled-up window down.</summary>
    public const string RollDownAllAction = "rollDownAll";

    private const int RollDownAllItem = 1;

    // How long the host waits for the feature's thread as it exits.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time;
    private readonly ILogger<WindowShadingFeature> _logger;
    private readonly ShadedWindows _set;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly WindowRequests _requests;
    private readonly Func<IShadingSystem> _recoverySystem;
    private WindowShadingThread? _thread;
    private WindowShadingThread? _stopped;
    private bool _failed;
    private bool _recovered;

    /// <param name="set">The rolled-up windows, which Minimize to Tray leaves alone.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which aren't rolled up.</param>
    /// <param name="tabs">Window Tabs' tabs, which aren't rolled up.</param>
    /// <param name="requests">Where Title Bar Actions asks for a window to be rolled up.</param>
    public WindowShadingFeature(TimeProvider time, ILogger<WindowShadingFeature> logger, ShadedWindows set, DropDownWindows dropDown, WindowTabsWindows tabs, WindowRequests requests)
        : this(time, logger, set, dropDown, tabs, requests, () => new WindowsShadingSystem(null, action => action(), dropDown, tabs))
    {
    }

    /// <param name="recoverySystem">Where the windows left marked are found, as tests supply it.</param>
    public WindowShadingFeature(TimeProvider time, ILogger<WindowShadingFeature> logger, ShadedWindows set, DropDownWindows dropDown, WindowTabsWindows tabs, WindowRequests requests, Func<IShadingSystem> recoverySystem)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(recoverySystem);

        _time = time;
        _logger = logger;
        _set = set;
        _dropDown = dropDown;
        _tabs = tabs;
        _requests = requests;
        _recoverySystem = recoverySystem;

        // A crash ends the host without stopping the feature, so the rolled-up windows are rolled down first.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static FeatureInfo Definition { get; } = new("windowShading", WindowsModule.Id, "WindowShadingName", "WindowShadingDescription", "");

    /// <summary>
    /// Whether this PC can run Window Shading, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public void Apply(JsonElement? settings)
    {
        // Windows left rolled up by a PC Companion that ended without rolling them down are rolled
        // down, on or off; it reads every window's properties without waiting for any program.
        if (!_recovered)
        {
            _recovered = true;
            var recovered = ShadingController.Recover(_recoverySystem());
            if (recovered > 0)
            {
                LogRecovered(_logger, recovered);
            }
        }

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

        WindowShadingSettings read;
        try
        {
            read = WindowShadingSettings.Read(settings);
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
            _thread = new WindowShadingThread(read, _time, _logger, _set, _dropDown, _tabs, _stopped);
            _stopped = null;
            _requests.SetRollUp(_thread.RollUp);
        }
        else
        {
            _thread.Update(read);
        }
    }

    /// <summary>The actions Window Shading offers to shortcuts and mouse buttons, named in the user's language.</summary>
    public static IReadOnlyList<OfferedAction> OfferedActions() =>
    [
        new(new FeatureAction(Definition.Id, RollUpFrontAction), Strings.WindowShadingRollUpFront) { OnWindowInFront = true },
        new(new FeatureAction(Definition.Id, RollDownLastAction), Strings.WindowShadingRollDownLast),
        new(new FeatureAction(Definition.Id, RollDownAllAction), Strings.WindowShadingRollDownAll),
    ];

    public bool CanRun(FeatureAction action) => _thread is not null && ActionOf(action) is not null;

    public bool OnWindowInFront(FeatureAction action) => ActionOf(action) == ShadeAction.RollUpFront;

    public void Run(FeatureAction action)
    {
        if (ActionOf(action) is { } shade)
        {
            _thread?.Run(shade);
        }
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems() =>
        _thread is { Count: > 0 } ? [new HostMenuItem(RollDownAllItem, Strings.WindowShadingRollDownAll)] : [];

    public void ChooseMenuItem(int id)
    {
        if (id == RollDownAllItem)
        {
            _thread?.Run(ShadeAction.RollDownAll);
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    private static ShadeAction? ActionOf(FeatureAction action) =>
        action.FeatureId != Definition.Id || action.Target is not null ? null : action.ActionId switch
        {
            RollUpFrontAction => ShadeAction.RollUpFront,
            RollDownLastAction => ShadeAction.RollDownLast,
            RollDownAllAction => ShadeAction.RollDownAll,
            _ => null,
        };

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _requests.SetRollUp(null);
        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    // Runs on the crashing thread; nothing here may throw.
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _thread?.ReleaseNow();
        }
        catch (Exception exception)
        {
            LogReleaseFailed(_logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading rolled down {Count} windows left rolled up.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Shading couldn't roll the windows down as PC Companion crashed.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Shading settings can't be read, so Window Shading is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Shading didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
