using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;
using HideAppChoice = PCCompanion.Modules.Windows.HideApps.HideAppShortcut;
using HideOthersChoice = PCCompanion.Modules.Windows.HideApps.HideOthersShortcut;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// Minimizes every window of the app in front, or of every other app, with one shortcut, and
/// brings them back as they were. See docs/features/windows/hide-apps.md.
/// </summary>
public sealed partial class HideAppsFeature : IFeature, IHostMenuItems, IFeatureActions
{
    // How long the host waits as it exits for the feature's thread to end.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private const int ShowAllItem = 1;

    private readonly TimeProvider _time;
    private readonly ILogger<HideAppsFeature> _logger;
    private readonly HideAppsMinimized _minimized;
    private readonly DropDownWindows _dropDown;
    private readonly WindowRequests _requests;
    private readonly ShortcutRefusals _refusals;
    private HideAppsThread? _thread;
    private HideAppsThread? _stopped;
    private bool _failed;

    /// <param name="minimized">The windows Hide Apps minimized, shared with Minimize to Tray.</param>
    /// <param name="dropDown">The drop-down windows, which Hide Apps leaves alone.</param>
    /// <param name="requests">Where Title Bar Actions passes on windows whose apps to hide.</param>
    /// <param name="refusals">Where the feature says which of its shortcuts Windows refused.</param>
    public HideAppsFeature(TimeProvider time, ILogger<HideAppsFeature> logger, HideAppsMinimized minimized, DropDownWindows dropDown, WindowRequests requests, ShortcutRefusals refusals)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(minimized);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(refusals);

        _time = time;
        _logger = logger;
        _minimized = minimized;
        _dropDown = dropDown;
        _requests = requests;
        _refusals = refusals;
    }

    /// <summary>Hide app's name in <see cref="ShortcutRefusals"/>.</summary>
    public const string HideAppShortcut = "hideApp";

    /// <summary>Hide others' name in <see cref="ShortcutRefusals"/>.</summary>
    public const string HideOthersShortcut = "hideOthers";

    /// <summary>Show all hidden windows' action ID; Hide app and Hide others use their shortcuts' names.</summary>
    public const string ShowAllAction = "showAll";

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
            _thread = new HideAppsThread(read, _time, _logger, _minimized, _dropDown, _stopped, _refusals);
            _stopped = null;
            _requests.SetHideApps(_thread.HideApp);
        }
        else
        {
            _thread.Update(read);
        }
    }

    /// <summary>The actions Hide Apps offers to shortcuts and mouse buttons, named in the user's language.</summary>
    public static IReadOnlyList<OfferedAction> OfferedActions() =>
    [
        new(new FeatureAction(Definition.Id, HideAppShortcut), Strings.HideAppsHideAppHeader),
        new(new FeatureAction(Definition.Id, HideOthersShortcut), Strings.HideAppsHideOthersHeader),
        new(new FeatureAction(Definition.Id, ShowAllAction), Strings.HideAppsShowAll),
    ];

    /// <summary>The shortcuts Hide Apps registers with <paramref name="settings"/> while it's on, each with the action it runs.</summary>
    public static IReadOnlyList<(FeatureAction Action, ShortcutKeys Keys)> Shortcuts(HideAppsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var shortcuts = new List<(FeatureAction, ShortcutKeys)>(2);
        if (settings.HideAppShortcut == HideAppChoice.WindowsControlH)
        {
            shortcuts.Add((new FeatureAction(Definition.Id, HideAppShortcut), ShortcutKeys.OfVirtualKey(HotKeyModifiers.Windows | HotKeyModifiers.Control, HideAppsThread.KeyH)));
        }

        if (settings.HideOthersShortcut == HideOthersChoice.WindowsControlShiftH)
        {
            shortcuts.Add((new FeatureAction(Definition.Id, HideOthersShortcut), ShortcutKeys.OfVirtualKey(HotKeyModifiers.Windows | HotKeyModifiers.Control | HotKeyModifiers.Shift, HideAppsThread.KeyH)));
        }

        return shortcuts;
    }

    public bool CanRun(FeatureAction action) => _thread is not null && ActionOf(action) is not null;

    public void Run(FeatureAction action)
    {
        if (ActionOf(action) is { } hide)
        {
            _thread?.Run(hide);
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

    private static HideAction? ActionOf(FeatureAction action) =>
        action.FeatureId != Definition.Id || action.Target is not null ? null : action.ActionId switch
        {
            HideAppShortcut => HideAction.HideApp,
            HideOthersShortcut => HideAction.HideOthers,
            ShowAllAction => HideAction.ShowAll,
            _ => null,
        };

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _requests.SetHideApps(null);
        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Hide Apps settings can't be read, so Hide Apps is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hide Apps didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
