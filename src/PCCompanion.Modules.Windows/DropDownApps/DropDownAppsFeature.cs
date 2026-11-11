using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// Slides a chosen app's window down from the top of the screen with a shortcut, and back up out of
/// the way with the same shortcut. See docs/features/windows/drop-down-apps.md.
/// </summary>
public sealed partial class DropDownAppsFeature : IFeature, IFeatureActions
{
    // How long the host waits as it exits for the windows to go back on the taskbar.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger<DropDownAppsFeature> _logger;
    private readonly Func<IDropDownSystem> _recoverySystem;
    private readonly DropDownWindows _shared;
    private readonly DropDownResults _results;
    private readonly ShortcutRefusals _refusals;
    private DropDownAppsThread? _thread;
    private DropDownAppsSettings _settings = new();
    private SnapshotPipe? _resultsPipe;
    private DropDownAppsThread? _stopped;
    private bool _failed;
    private bool _recovered;
    private bool _askedToEndFirst;

    /// <param name="shared">The drop-down windows, which Hide Apps and Minimize to Tray leave alone.</param>
    /// <param name="refusals">Where the feature says which of its shortcuts Windows refused.</param>
    public DropDownAppsFeature(IHostCommands commands, TimeProvider time, ILogger<DropDownAppsFeature> logger, DropDownWindows shared, ShortcutRefusals refusals)
        : this(commands, time, logger, shared, refusals, () => new WindowsDropDownSystem(null, action => action(), null))
    {
    }

    /// <param name="shared">The drop-down windows, which Hide Apps and Minimize to Tray leave alone.</param>
    /// <param name="refusals">Where the feature says which of its shortcuts Windows refused.</param>
    /// <param name="recoverySystem">Where the windows left marked are found, as tests supply it.</param>
    public DropDownAppsFeature(IHostCommands commands, TimeProvider time, ILogger<DropDownAppsFeature> logger, DropDownWindows shared, ShortcutRefusals refusals, Func<IDropDownSystem> recoverySystem)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(refusals);
        ArgumentNullException.ThrowIfNull(recoverySystem);

        _commands = commands;
        _time = time;
        _logger = logger;
        _shared = shared;
        _refusals = refusals;
        _recoverySystem = recoverySystem;
        _results = new DropDownResults(time);

        // A crash ends the host without stopping the feature, so the windows go back on the taskbar first.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    /// <summary>The action that shows or hides a listed app, whose target is the app's app ID or program.</summary>
    public const string ShowOrHideAction = "showOrHide";

    public static FeatureInfo Definition { get; } = new("dropDownApps", WindowsModule.Id, "DropDownAppsName", "DropDownAppsDescription", "");

    /// <summary>
    /// Whether this PC can run Drop-down Apps, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    /// <summary>
    /// The actions Drop-down Apps offers to shortcuts and mouse buttons: showing or hiding each
    /// listed app, named in the user's language.
    /// </summary>
    public static IReadOnlyList<OfferedAction> OfferedActions(DropDownAppsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            .. settings.Apps
                .Where(app => app.Target is not null)
                .Select(app => new OfferedAction(
                    new FeatureAction(Definition.Id, ShowOrHideAction, app.Target),
                    string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.DropDownAppsActionShowOrHide), app.Name))),
        ];
    }

    public bool CanRun(FeatureAction action) =>
        _thread is not null
        && action.FeatureId == Definition.Id
        && action.ActionId == ShowOrHideAction
        && _settings.Apps.Any(app => app.IsTarget(action.Target));

    public void Run(FeatureAction action)
    {
        if (CanRun(action))
        {
            _thread?.PressApp(action.Target!);
        }
    }

    public void Apply(JsonElement? settings)
    {
        // Windows left marked by a PC Companion that ended without bringing them back come back on
        // the taskbar, on or off; it reads every window's properties without waiting for any program.
        if (!_recovered)
        {
            _recovered = true;
            var recovered = DropDownController.Recover(_recoverySystem());
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

        DropDownAppsSettings read;
        try
        {
            read = DropDownAppsSettings.Read(settings);
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

        // Windows then asks the host before other apps when the session ends, so the hidden
        // windows are back on the taskbar before their apps are asked to close.
        if (!_askedToEndFirst)
        {
            _askedToEndFirst = true;
            if (!ShutdownOrder.AskFirst())
            {
                LogShutdownOrderRefused(_logger);
            }
        }

        _settings = read;
        if (_thread is null)
        {
            _thread = new DropDownAppsThread(read, _commands, _time, _logger, _shared, _results, _stopped, _refusals);
            _stopped = null;
            _resultsPipe = ServeResults();
        }
        else
        {
            _thread.Update(read);
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

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
        _resultsPipe?.Dispose();
        _resultsPipe = null;
        _results.Clear();
    }

    // The settings app shows the results while the feature runs; without the pipe, shortcuts still work.
    private SnapshotPipe? ServeResults()
    {
        try
        {
            return SnapshotPipe.Serve(DropDownResults.PipeName, _results.ToJson, exception => LogResultsFailed(_logger, exception));
        }
        catch (Win32Exception exception)
        {
            LogResultsFailed(_logger, exception);
            return null;
        }
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drop-down Apps brought back {Count} windows left hidden.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Drop-down Apps settings can't be read, so Drop-down Apps is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to ask PC Companion first when the session ends.")]
    private static partial void LogShutdownOrderRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drop-down Apps didn't stop within {Seconds} s, so windows may stay hidden until PC Companion starts again.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drop-down Apps couldn't bring its windows back on the taskbar as PC Companion crashed.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drop-down Apps can't give the settings app its recent results.")]
    private static partial void LogResultsFailed(ILogger logger, Exception exception);
}
