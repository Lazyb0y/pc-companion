using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Groups windows of different apps into one window with tabs, and switches between them with a
/// click or a shortcut. See docs/features/windows/window-tabs.md.
/// </summary>
public sealed partial class WindowTabsFeature : IFeature, IFeatureActions
{
    /// <summary>The name of the switch shortcuts in <see cref="ShortcutRefusals"/>.</summary>
    public const string SwitchShortcutsName = "switchTabs";

    /// <summary>The name of the group shortcuts in <see cref="ShortcutRefusals"/>.</summary>
    public const string GroupShortcutsName = "groupTabs";

    // The actions' IDs, which shortcuts and mouse buttons store, so never change one.
    public const string NextTabAction = "nextTab";
    public const string PreviousTabAction = "previousTab";
    public const string MoveTabForwardAction = "moveTabForward";
    public const string MoveTabBackAction = "moveTabBack";
    public const string AddToGroupBehindAction = "addToGroupBehind";
    public const string TakeOutOfGroupAction = "takeOutOfGroup";

    // How long the host waits as it exits for the hidden tabs to go back on the taskbar.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger<WindowTabsFeature> _logger;
    private readonly WindowTabsWindows _shared;
    private readonly DropDownWindows _dropDown;
    private readonly ShortcutRefusals _refusals;
    private readonly Func<ITabsSystem> _recoverySystem;
    private WindowTabsThread? _thread;
    private WindowTabsThread? _stopped;
    private bool _failed;
    private bool _recovered;
    private bool _askedToEndFirst;

    /// <param name="shared">The tabs, which Minimize to Tray and Drop-down Apps leave alone.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    /// <param name="refusals">Where the feature says whether Windows refused its shortcuts.</param>
    public WindowTabsFeature(IHostCommands commands, TimeProvider time, ILogger<WindowTabsFeature> logger, WindowTabsWindows shared, DropDownWindows dropDown, ShortcutRefusals refusals)
        : this(commands, time, logger, shared, dropDown, refusals, () => new WindowsTabsSystem(null, action => action(), null))
    {
    }

    /// <param name="shared">The tabs, which Minimize to Tray and Drop-down Apps leave alone.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    /// <param name="refusals">Where the feature says whether Windows refused its shortcuts.</param>
    /// <param name="recoverySystem">Where the windows left marked are found, as tests supply it.</param>
    public WindowTabsFeature(IHostCommands commands, TimeProvider time, ILogger<WindowTabsFeature> logger, WindowTabsWindows shared, DropDownWindows dropDown, ShortcutRefusals refusals, Func<ITabsSystem> recoverySystem)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(refusals);
        ArgumentNullException.ThrowIfNull(recoverySystem);

        _commands = commands;
        _time = time;
        _logger = logger;
        _shared = shared;
        _dropDown = dropDown;
        _refusals = refusals;
        _recoverySystem = recoverySystem;

        // A crash ends the host without stopping the feature, so the hidden tabs go back on the taskbar first.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static FeatureInfo Definition { get; } = new("windowTabs", WindowsModule.Id, "WindowTabsName", "WindowTabsDescription", "\uE7C4");

    /// <summary>
    /// Whether this PC can run Window Tabs, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public bool CanRun(FeatureAction action) => _thread is not null && ControllerAction(action) is not null;

    // Every one acts on the window in front, as the feature's shortcuts do.
    public bool OnWindowInFront(FeatureAction action) => ControllerAction(action) is not null;

    // The feature's thread runs it, as for its own shortcut.
    public void Run(FeatureAction action)
    {
        if (ControllerAction(action) is { } run)
        {
            _thread?.Run(run);
        }
    }

    public void Apply(JsonElement? settings)
    {
        // Windows left hidden by a PC Companion that ended without bringing them back come back on
        // the taskbar, on or off; it reads every window's properties without waiting for any program.
        if (!_recovered)
        {
            _recovered = true;
            var recovered = TabsController.Recover(_recoverySystem());
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

        WindowTabsSettings read;
        try
        {
            read = WindowTabsSettings.Read(settings);
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

        // Windows then asks the host before other apps when the session ends, so the hidden tabs
        // are back on the taskbar before their apps are asked to close.
        if (!_askedToEndFirst)
        {
            _askedToEndFirst = true;
            if (!ShutdownOrder.AskFirst())
            {
                LogShutdownOrderRefused(_logger);
            }
        }

        if (_thread is null)
        {
            _thread = new WindowTabsThread(read, _time, _logger, _commands, _shared, _dropDown, _stopped, _refusals);
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
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    private static Action<TabsController>? ControllerAction(FeatureAction action) =>
        action.FeatureId != Definition.Id || action.Target is not null ? null : action.ActionId switch
        {
            NextTabAction => static controller => controller.Next(),
            PreviousTabAction => static controller => controller.Previous(),
            MoveTabForwardAction => static controller => controller.MoveForward(),
            MoveTabBackAction => static controller => controller.MoveBack(),
            AddToGroupBehindAction => static controller => controller.GroupFrontWithBehind(),
            TakeOutOfGroupAction => static controller => controller.TakeOutFront(),
            _ => null,
        };

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs brought back {Count} windows left hidden.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Tabs settings can't be read, so Window Tabs is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to ask PC Companion first when the session ends.")]
    private static partial void LogShutdownOrderRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Tabs didn't stop within {Seconds} s, so windows may stay hidden until PC Companion starts again.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Tabs couldn't bring its hidden tabs back on the taskbar as PC Companion crashed.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);
}
