using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
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

    // How long the host waits as it exits for the hidden tabs to go back on the taskbar, and then
    // for the kept groups to be written.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(1);

    // How long after the host starts the groups read from window-tabs.json wait for their windows.
    private static readonly TimeSpan RegroupTime = TimeSpan.FromSeconds(60);

    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger<WindowTabsFeature> _logger;
    private readonly WindowTabsWindows _shared;
    private readonly DropDownWindows _dropDown;
    private readonly WindowRequests _requests;
    private readonly ShortcutRefusals _refusals;
    private readonly Func<ITabsSystem> _recoverySystem;
    private readonly KeptGroupsStore _store;
    private readonly bool _isUacElevated;
    private readonly long _regroupUntil;

    // The file's reads, writes and deletes run on the thread pool one after another, in order,
    // asked for from the host's UI thread and the feature's thread.
    private readonly Lock _fileGate = new();
    private Task _fileWork = Task.CompletedTask;

    // Whether the file was deleted since the last write, so it isn't deleted again at each save of the settings.
    private bool _fileDeleted;
    private WindowTabsThread? _thread;
    private WindowTabsThread? _stopped;
    private bool _failed;
    private bool _recovered;
    private bool _askedToEndFirst;

    /// <param name="dataFolder">Where <c>window-tabs.json</c> keeps the groups for after a restart.</param>
    /// <param name="shared">The tabs, which Minimize to Tray and Drop-down Apps leave alone.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    /// <param name="requests">Where Title Bar Actions passes on windows to group or take out.</param>
    /// <param name="refusals">Where the feature says whether Windows refused its shortcuts.</param>
    public WindowTabsFeature(IHostCommands commands, TimeProvider time, ILogger<WindowTabsFeature> logger, DataFolder dataFolder, WindowTabsWindows shared, DropDownWindows dropDown, WindowRequests requests, ShortcutRefusals refusals)
        : this(commands, time, logger, dataFolder, Elevation.IsUacElevated, shared, dropDown, requests, refusals, () => new WindowsTabsSystem(null, action => action(), null))
    {
    }

    /// <param name="dataFolder">Where <c>window-tabs.json</c> keeps the groups for after a restart.</param>
    /// <param name="isUacElevated">Whether the host runs as administrator after a UAC prompt, when the file isn't used.</param>
    /// <param name="shared">The tabs, which Minimize to Tray and Drop-down Apps leave alone.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which can't be tabs.</param>
    /// <param name="requests">Where Title Bar Actions passes on windows to group or take out.</param>
    /// <param name="refusals">Where the feature says whether Windows refused its shortcuts.</param>
    /// <param name="recoverySystem">Where the windows left marked are found, as tests supply it.</param>
    public WindowTabsFeature(IHostCommands commands, TimeProvider time, ILogger<WindowTabsFeature> logger, DataFolder dataFolder, bool isUacElevated, WindowTabsWindows shared, DropDownWindows dropDown, WindowRequests requests, ShortcutRefusals refusals, Func<ITabsSystem> recoverySystem)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dataFolder);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(refusals);
        ArgumentNullException.ThrowIfNull(recoverySystem);

        _commands = commands;
        _time = time;
        _logger = logger;
        _shared = shared;
        _dropDown = dropDown;
        _requests = requests;
        _refusals = refusals;
        _recoverySystem = recoverySystem;
        _store = new KeptGroupsStore(Path.Join(dataFolder.Path, KeptGroupsStore.FileName));
        _isUacElevated = isUacElevated;
        _regroupUntil = time.GetTimestamp() + (long)(RegroupTime.TotalSeconds * time.TimestampFrequency);

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

    /// <summary>The actions Window Tabs offers to shortcuts and mouse buttons, named in the user's language.</summary>
    public static IReadOnlyList<OfferedAction> OfferedActions() =>
    [
        new(new FeatureAction(Definition.Id, NextTabAction), Strings.WindowTabsActionNextTab) { OnWindowInFront = true },
        new(new FeatureAction(Definition.Id, PreviousTabAction), Strings.WindowTabsActionPreviousTab) { OnWindowInFront = true },
        new(new FeatureAction(Definition.Id, MoveTabForwardAction), Strings.WindowTabsActionMoveTabForward) { OnWindowInFront = true },
        new(new FeatureAction(Definition.Id, MoveTabBackAction), Strings.WindowTabsActionMoveTabBack) { OnWindowInFront = true },
        new(new FeatureAction(Definition.Id, AddToGroupBehindAction), Strings.WindowTabsActionAddToGroupBehind) { OnWindowInFront = true },
        new(new FeatureAction(Definition.Id, TakeOutOfGroupAction), Strings.WindowTabsMoveOutOfGroup) { OnWindowInFront = true },
    ];

    /// <summary>The shortcuts Window Tabs registers with <paramref name="settings"/> while it's on, each with the action it runs.</summary>
    public static IReadOnlyList<(FeatureAction Action, ShortcutKeys Keys)> Shortcuts(WindowTabsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var shortcuts = new List<(FeatureAction, ShortcutKeys)>();
        HotKeyModifiers? switchKeys = settings.SwitchShortcuts switch
        {
            TabSwitchShortcuts.WindowsControlPage => HotKeyModifiers.Windows | HotKeyModifiers.Control,
            TabSwitchShortcuts.WindowsAltPage => HotKeyModifiers.Windows | HotKeyModifiers.Alt,
            _ => null,
        };

        if (switchKeys is { } modifiers)
        {
            shortcuts.Add((new FeatureAction(Definition.Id, NextTabAction), ShortcutKeys.OfVirtualKey(modifiers, WindowTabsThread.PageDown)));
            shortcuts.Add((new FeatureAction(Definition.Id, PreviousTabAction), ShortcutKeys.OfVirtualKey(modifiers, WindowTabsThread.PageUp)));
            shortcuts.Add((new FeatureAction(Definition.Id, MoveTabForwardAction), ShortcutKeys.OfVirtualKey(modifiers | HotKeyModifiers.Shift, WindowTabsThread.PageDown)));
            shortcuts.Add((new FeatureAction(Definition.Id, MoveTabBackAction), ShortcutKeys.OfVirtualKey(modifiers | HotKeyModifiers.Shift, WindowTabsThread.PageUp)));
        }

        if (settings.GroupShortcuts == TabGroupShortcuts.WindowsControlT)
        {
            var groupKeys = HotKeyModifiers.Windows | HotKeyModifiers.Control;
            shortcuts.Add((new FeatureAction(Definition.Id, AddToGroupBehindAction), ShortcutKeys.OfVirtualKey(groupKeys, WindowTabsThread.KeyT)));
            shortcuts.Add((new FeatureAction(Definition.Id, TakeOutOfGroupAction), ShortcutKeys.OfVirtualKey(groupKeys | HotKeyModifiers.Shift, WindowTabsThread.KeyT)));
        }

        return shortcuts;
    }

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

        // Turning the feature off forgets the kept groups.
        if (!read.Enabled)
        {
            DeleteFile();
            Stop();
            return;
        }

        if (!read.KeepGroups)
        {
            DeleteFile();
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

        var keeps = read.KeepGroups && !_isUacElevated;
        if (_thread is null)
        {
            var thread = new WindowTabsThread(read, _time, _logger, _commands, _shared, _dropDown, _stopped, _refusals, keeps, ReadFile, SaveFile);
            _thread = thread;
            _stopped = null;
            _requests.SetWindowTabs(
                window => thread.RunFromTitleBar(controller => controller.GroupWithBehind(window)),
                window => thread.RunFromTitleBar(controller => controller.TakeOutOf(window)));
            _requests.SetGroupWith((window, target) => thread.RunFromTitleBar(controller => controller.GroupWith(window, target)));
        }
        else
        {
            _thread.Keeps = keeps;
            _thread.Update(read);
        }
    }

    // The host disposes features twice as it exits, so the thread and the file are waited for only once.
    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
        Task fileWork;
        lock (_fileGate)
        {
            fileWork = _fileWork;
            _fileWork = Task.CompletedTask;
        }

        if (!fileWork.Wait(FileTimeout))
        {
            LogFileNotDoneInTime(_logger, FileTimeout.TotalSeconds);
        }
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

        _requests.SetWindowTabs(null, null);
        _requests.SetGroupWith(null);
        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    // The feature's thread asks for the kept groups as it starts: during the minute after the host
    // started, they're read and passed back to it to be made again.
    private void ReadFile(WindowTabsThread thread)
    {
        if (_isUacElevated || _time.GetTimestamp() >= _regroupUntil)
        {
            return;
        }

        OnFileThread(() =>
        {
            var groups = _store.Load(out var readable);
            if (!readable)
            {
                LogUnreadableFile(_logger);
            }

            if (groups.Count > 0)
            {
                thread.StartRegrouping(groups, _regroupUntil);
            }
        });
    }

    // The feature's thread passes the kept groups after a change and as it stops. They're written
    // only if the thread still keeps them when it's their turn, so a write asked for before the
    // feature or Keep groups after a restart was turned off doesn't bring the file back.
    private void SaveFile(WindowTabsThread thread, IReadOnlyList<KeptGroup> groups)
    {
        lock (_fileGate)
        {
            _fileDeleted = false;
        }

        OnFileThread(() =>
        {
            if (!thread.Keeps)
            {
                return;
            }

            try
            {
                _store.Save(groups);
                LogFileSaved(_logger, groups.Count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFileNotSaved(_logger, exception);
            }
        });
    }

    // Forgetting deletes the file, once, unless the host runs as administrator after a UAC
    // prompt, when a program without admin rights could have put a link in its place. Neither the
    // thread nor one still stopping writes the groups again.
    private void DeleteFile()
    {
        if (_thread is not null)
        {
            _thread.Keeps = false;
        }

        if (_stopped is not null)
        {
            _stopped.Keeps = false;
        }

        lock (_fileGate)
        {
            if (_fileDeleted || _isUacElevated)
            {
                return;
            }

            _fileDeleted = true;
        }

        OnFileThread(() =>
        {
            try
            {
                _store.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFileNotDeleted(_logger, exception);
            }
        });
    }

    private void OnFileThread(Action work)
    {
        lock (_fileGate)
        {
            _fileWork = _fileWork.ContinueWith(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception exception)
                    {
                        LogFileWorkFailed(_logger, exception);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Tabs' kept groups can't be read, so none are made again; the file is replaced at the next save.")]
    private static partial void LogUnreadableFile(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Tabs saved {Count} kept groups.")]
    private static partial void LogFileSaved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Tabs couldn't save its kept groups.")]
    private static partial void LogFileNotSaved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Tabs couldn't delete its kept groups.")]
    private static partial void LogFileNotDeleted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Tabs' work on its kept groups failed.")]
    private static partial void LogFileWorkFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Tabs' kept groups weren't written within {Seconds} s.")]
    private static partial void LogFileNotDoneInTime(ILogger logger, double seconds);
}
