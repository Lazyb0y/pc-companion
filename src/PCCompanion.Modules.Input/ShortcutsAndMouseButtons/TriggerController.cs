using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// Decides what the feature's thread registers and claims, and what a press runs, as "Keyboard
/// shortcuts", "Mouse buttons" and "Not in these apps" in
/// docs/features/input/shortcuts-and-mouse-buttons.md say: shortcuts by key position, without the
/// ones that type a character with AltGr in one of the user's layouts, and without the ones whose
/// listed app is in front; mouse buttons in the shared pointer hook, except over the windows of
/// their listed apps, and not while the window in front is full screen. Use it on the feature's
/// thread; <paramref name="post"/> runs an action on that thread later.
/// </summary>
public sealed partial class TriggerController : IDisposable, ITriggerEvents
{
    /// <summary>How often a wait for released keys looks again.</summary>
    public static readonly TimeSpan ReleasePoll = TimeSpan.FromMilliseconds(50);

    /// <summary>How long after the keys are released Turn off displays and Start the screen saver run.</summary>
    public static readonly TimeSpan AfterRelease = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a wait for released keys lasts at most; the action then runs anyway.</summary>
    public static readonly TimeSpan LongestHold = TimeSpan.FromSeconds(10);

    /// <summary>How long after raw input reported a click the hook is asked whether it saw it.</summary>
    public static readonly TimeSpan HookCheckDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>How long changes of the front window's place are gathered before it's checked for full screen.</summary>
    public static readonly TimeSpan MoveCheckDelay = TimeSpan.FromMilliseconds(100);

    // Every display.
    private static readonly Rectangle Everywhere = new(-0x8000, -0x8000, 0x10000, 0x10000);

    private readonly ITriggerSystem _system;
    private readonly ShortcutKeeper _keeper;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private TriggerPlan _plan = new([], true);
    private Dictionary<KeyPositionShortcut, PlannedTrigger> _byShortcut = [];
    private HashSet<KeyPositionShortcut> _typesWithAltGr = [];
    private IReadOnlyList<nint> _layouts = [];
    private HashSet<KeyPositionShortcut>? _configured;
    private ReleaseWait? _wait;
    private Dictionary<PointerTrigger, PlannedTrigger> _byButton = [];

    // The apps of the top-level windows read so far, while a mouse button trigger lists apps.
    private readonly Dictionary<nint, AppOfWindow> _apps = [];
    private bool _taskbarRead;
    private bool _fullScreen;
    private ITimer? _moveCheck;
    private bool _disposed;

    public TriggerController(ITriggerSystem system, IShortcutEnvironment shortcuts, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _time = time;
        _post = post;
        _logger = logger;
        _keeper = new ShortcutKeeper(shortcuts, time, post);
        _keeper.Pressed += (_, shortcut) => Pressed(shortcut);
        _keeper.Registered += (_, registered) => LogRegistered(_logger, registered.Shortcut.Modifiers, registered.Shortcut.ScanCode, registered.VirtualKey);
        _keeper.Refused += (_, refused) => LogRefused(_logger, NameOf(refused.Shortcut), refused.VirtualKey);
        _keeper.RefusedShortcutsChanged += (_, _) => _system.ReportRefused(_keeper.RefusedShortcuts.Select(NameOf));
    }

    /// <summary>The shortcuts registered now, or tried: the plan's, without those Windows can't have now.</summary>
    public IReadOnlyCollection<KeyPositionShortcut> Wanted => _configured ?? [];

    /// <summary>The mouse buttons claimed now, or <see langword="null"/>, as while an app is full screen.</summary>
    public PointerClaim? Claimed { get; private set; }

    private bool ListsAppsForButtons => _byButton.Values.Any(trigger => trigger.ExcludedApps.Count > 0);

    private bool WatchesFullScreen => _byButton.Count > 0 && _plan.PauseMouseButtonsInFullScreen;

    /// <summary>Registers the plan's shortcuts, also those Windows refused before, in place of the earlier plan's.</summary>
    public void Apply(TriggerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _plan = plan;
        _byShortcut = plan.Triggers.Where(trigger => trigger.Shortcut is not null).ToDictionary(trigger => trigger.Shortcut!.Value);
        FindAltGrCharacters();
        Configure(again: true);
        LogApplied(_logger, plan.Triggers.Count, _byShortcut.Count, _typesWithAltGr.Count);

        _byButton = plan.Triggers.Where(trigger => trigger.Input.Button is not null).ToDictionary(trigger => PointerTriggerOf(trigger.Input));
        if (ListsAppsForButtons && !_taskbarRead)
        {
            _taskbarRead = true;
            _ = ReadTaskbarAsync();
        }

        _system.WatchFront(WatchesFullScreen);
        CheckFullScreen();
        Claim();
    }

    /// <summary>The pointer hook kept a click for the claim: it runs the trigger's action.</summary>
    public void Kept(PointerTrigger trigger)
    {
        if (_disposed || !_byButton.TryGetValue(trigger, out var planned))
        {
            return;
        }

        // Keeps Start from opening as the Windows key is released and Alt from opening a menu bar,
        // and lets the action bring a window to the front.
        _system.PressMaskKey();
        LogRan(_logger, planned.Action.FeatureId, planned.Action.ActionId, planned.Input.Name);
        Run(planned, () => _system.IsHeld(trigger));
    }

    /// <summary>
    /// Raw input reported a click: a moment later, the hook is asked whether it saw it. One it never
    /// saw while it was claimed means Windows removed it, so it's set again, and that click runs nothing.
    /// </summary>
    public void RawPress(PointerButton button, uint time)
    {
        if (_disposed || Claimed is not { } claim || !claim.Triggers.Any(trigger => trigger.Button == button))
        {
            return;
        }

        ITimer? check = null;
        check = _time.CreateTimer(
            _ => _post(() =>
            {
                check?.Dispose();
                if (!_disposed && Claimed is not null && _system.Outcome(time) == PointerInputOutcome.NotSeen)
                {
                    LogHookRemoved(_logger);
                    _system.SetHookAgain();
                }
            }),
            null,
            HookCheckDelay,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>The window in front moved or changed size, which a full-screen check looks at a moment later.</summary>
    public void FrontMoved()
    {
        if (_disposed || _moveCheck is not null)
        {
            return;
        }

        _moveCheck = _time.CreateTimer(
            _ => _post(() =>
            {
                _moveCheck?.Dispose();
                _moveCheck = null;
                if (!_disposed)
                {
                    CheckFullScreen();
                    Claim();
                }
            }),
            null,
            MoveCheckDelay,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>The pointer hook's thread failed, which stops the feature.</summary>
    public void HookFailed(Exception exception) => throw new InvalidOperationException("The pointer hook failed.", exception);

    /// <summary>
    /// Another window came to the front: a listed app may be in front, or gone, the layout may have
    /// changed, and the user may have added a layout.
    /// </summary>
    public void OnForeground()
    {
        if (_disposed)
        {
            return;
        }

        if (!_system.InstalledLayouts().SequenceEqual(_layouts))
        {
            FindAltGrCharacters();
        }

        Configure(again: false);
        _keeper.Check();

        if (_byButton.Count == 0)
        {
            return;
        }

        if (ListsAppsForButtons)
        {
            var front = _system.FrontWindow();
            if (front != 0 && !_apps.ContainsKey(front))
            {
                _apps[front] = _system.AppOf(front);
            }

            foreach (var closed in _apps.Keys.Where(window => !_system.Exists(window)).ToList())
            {
                _apps.Remove(closed);
            }
        }

        _system.WatchFront(WatchesFullScreen);
        CheckFullScreen();
        Claim();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wait?.Dispose();
        _wait = null;
        _moveCheck?.Dispose();
        _moveCheck = null;
        _keeper.Dispose();
        if (Claimed is not null)
        {
            Claimed = null;
            _system.Claim(null);
        }

        _system.WatchFront(false);
    }

    private static PointerTrigger PointerTriggerOf(TriggerInput input) => new(
        input.Button switch
        {
            TriggerButton.Back => PointerButton.Back,
            TriggerButton.Forward => PointerButton.Forward,
            _ => PointerButton.Middle,
        },
        (input.Modifiers.HasFlag(HotKeyModifiers.Shift) ? PointerKeys.Shift : PointerKeys.None)
        | (input.Modifiers.HasFlag(HotKeyModifiers.Control) ? PointerKeys.Control : PointerKeys.None)
        | (input.Modifiers.HasFlag(HotKeyModifiers.Alt) ? PointerKeys.Alt : PointerKeys.None)
        | (input.Modifiers.HasFlag(HotKeyModifiers.Windows) ? PointerKeys.Windows : PointerKeys.None));

    // Whether two claims keep the same clicks, so an unchanged claim isn't made again.
    private static bool SameClaim(PointerClaim? first, PointerClaim? second) =>
        (first, second) switch
        {
            (null, null) => true,
            ({ } one, { } other) => one.Triggers.SetEquals(other.Triggers)
                && one.ExceptIn.Count == other.ExceptIn.Count
                && one.ExceptIn.All(pair => other.ExceptIn.TryGetValue(pair.Key, out var windows) && windows.SetEquals(pair.Value)),
            _ => false,
        };

    private async Task ReadTaskbarAsync()
    {
        var windows = await _system.ReadTaskbarAppsAsync().ConfigureAwait(false);
        _post(() =>
        {
            if (_disposed)
            {
                return;
            }

            foreach (var (window, app) in windows)
            {
                _apps.TryAdd(window, app);
            }

            Claim();
        });
    }

    private void CheckFullScreen()
    {
        var fullScreen = WatchesFullScreen && _system.IsFrontFullScreen();
        if (fullScreen != _fullScreen)
        {
            _fullScreen = fullScreen;
            if (fullScreen)
            {
                LogPausedForFullScreen(_logger);
            }
            else
            {
                LogResumedAfterFullScreen(_logger);
            }
        }
    }

    // Claims the plan's mouse buttons, except over the windows of each one's listed apps, or none
    // while the window in front is full screen.
    private void Claim()
    {
        PointerClaim? claim = null;
        if (_byButton.Count > 0 && !_fullScreen)
        {
            var exceptIn = new Dictionary<PointerTrigger, IReadOnlySet<nint>>();
            foreach (var (trigger, planned) in _byButton)
            {
                if (planned.ExcludedApps.Count > 0)
                {
                    exceptIn[trigger] = _apps.Where(pair => planned.ExcludedApps.Any(listed => listed.Matches(pair.Value))).Select(pair => pair.Key).ToHashSet();
                }
            }

            claim = new PointerClaim(Everywhere, _byButton.Keys.ToHashSet()) { ExceptIn = exceptIn, NotWhileCaptured = true };
        }

        if (SameClaim(Claimed, claim))
        {
            return;
        }

        Claimed = claim;
        _system.Claim(claim);
        LogClaimed(_logger, claim?.Triggers.Count ?? 0);
    }

    // Registers the wanted shortcuts when they changed, or always when asked to try again.
    private void Configure(bool again)
    {
        var front = _plan.Triggers.Any(trigger => trigger.Shortcut is not null && trigger.ExcludedApps.Count > 0) ? _system.FrontApp() : null;
        var wanted = _byShortcut
            .Where(pair => !_typesWithAltGr.Contains(pair.Key) && !(front is { } app && pair.Value.ExcludedApps.Any(listed => listed.Matches(app))))
            .Select(pair => pair.Key)
            .ToHashSet();
        if (!again && _configured is { } configured && configured.SetEquals(wanted))
        {
            return;
        }

        var leftToApps = _byShortcut.Count - _typesWithAltGr.Count - wanted.Count;
        if (leftToApps > 0)
        {
            LogLeftToApp(_logger, leftToApps);
        }

        _configured = wanted;
        _keeper.Configure(wanted);
    }

    // Ctrl + Alt is AltGr, so such a shortcut would take a character from every app.
    private void FindAltGrCharacters()
    {
        _layouts = [.. _system.InstalledLayouts()];
        var typing = new HashSet<KeyPositionShortcut>();
        foreach (var (shortcut, trigger) in _byShortcut)
        {
            if (ShortcutRules.AltGrConflict(trigger.Input.Modifiers, trigger.Input.Key!, _layouts, _system.TypedWithAltGr) is not null)
            {
                typing.Add(shortcut);
                LogTypesWithAltGr(_logger, trigger.Input.Name);
            }
        }

        _typesWithAltGr = typing;
    }

    private void Pressed(KeyPositionShortcut shortcut)
    {
        if (_disposed || !_byShortcut.TryGetValue(shortcut, out var trigger))
        {
            return;
        }

        // The app in front got Alt but not the key, so releasing Alt would open its menu bar.
        if (shortcut.Modifiers.HasFlag(HotKeyModifiers.Alt) && _system.IsAltHeld())
        {
            _system.PressMaskKey();
        }

        LogRan(_logger, trigger.Action.FeatureId, trigger.Action.ActionId, trigger.Input.Name);
        Run(trigger, () => _system.IsHeld(shortcut));
    }

    private void Run(PlannedTrigger trigger, Func<bool> held)
    {
        if (trigger.Action.FeatureId != ShortcutsAndMouseButtonsFeature.Definition.Id)
        {
            _system.Run(trigger.Action);
            return;
        }

        switch (trigger.Action.ActionId)
        {
            case ShortcutsAndMouseButtonsFeature.LockAction:
                _system.Lock();
                break;

            // Any input turns the displays back on and ends the screen saver, also releasing the keys.
            case ShortcutsAndMouseButtonsFeature.TurnOffDisplaysAction:
                AfterReleased(held, _system.TurnOffDisplays);
                break;
            case ShortcutsAndMouseButtonsFeature.ScreenSaverAction:
                AfterReleased(held, _system.StartScreenSaver);
                break;
        }
    }

    // Looks every ReleasePoll until nothing is held, or LongestHold passed, then waits AfterRelease.
    // A later wait replaces an earlier one.
    private void AfterReleased(Func<bool> held, Action action)
    {
        _wait?.Dispose();
        var wait = new ReleaseWait(held, action, _time.GetTimestamp());
        _wait = wait;
        wait.Timer = _time.CreateTimer(_ => _post(() => Look(wait)), null, ReleasePoll, ReleasePoll);
    }

    private void Look(ReleaseWait wait)
    {
        if (_disposed || _wait != wait)
        {
            return;
        }

        if (!wait.Released)
        {
            if (wait.Held() && _time.GetElapsedTime(wait.Started) < LongestHold)
            {
                return;
            }

            wait.Released = true;
            wait.Timer?.Change(AfterRelease, Timeout.InfiniteTimeSpan);
            return;
        }

        _wait = null;
        wait.Dispose();
        wait.Action();
    }

    private string NameOf(KeyPositionShortcut shortcut) =>
        _byShortcut.TryGetValue(shortcut, out var trigger) ? trigger.Input.Name : FormattableString.Invariant($"{shortcut.Modifiers}+{shortcut.ScanCode:X2}");

    // An action that waits until the keys or the button that ran it are released.
    private sealed class ReleaseWait(Func<bool> held, Action action, long started) : IDisposable
    {
        public Func<bool> Held => held;

        public Action Action => action;

        public long Started => started;

        public ITimer? Timer { get; set; }

        public bool Released { get; set; }

        public void Dispose() => Timer?.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons applied its settings: {Triggers} triggers can be used, {Shortcuts} of them shortcuts, {AltGr} left out for AltGr.")]
    private static partial void LogApplied(ILogger logger, int triggers, int shortcuts, int altGr);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons registered {Modifiers} with the key of scan code {ScanCode:X2}, virtual key {VirtualKey:X2}.")]
    private static partial void LogRegistered(ILogger logger, HotKeyModifiers modifiers, uint scanCode, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused the Shortcuts & Mouse Buttons shortcut {Shortcut}, with virtual key {VirtualKey:X2}; another program may have it.")]
    private static partial void LogRefused(ILogger logger, string shortcut, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shortcut {Shortcut} isn't registered: Ctrl + Alt with its key types a character with AltGr in one of the keyboard layouts, which it would take from every app.")]
    private static partial void LogTypesWithAltGr(ILogger logger, string shortcut);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons left {Count} shortcuts to the app in front, which their triggers skip.")]
    private static partial void LogLeftToApp(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons runs {FeatureId}'s action {ActionId}, for {Input}.")]
    private static partial void LogRan(ILogger logger, string featureId, string actionId, string input);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons keeps {Count} mouse buttons from the apps.")]
    private static partial void LogClaimed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons paused its mouse buttons, since the window in front is full screen.")]
    private static partial void LogPausedForFullScreen(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Shortcuts & Mouse Buttons resumed its mouse buttons, since the window in front isn't full screen.")]
    private static partial void LogResumedAfterFullScreen(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows removed the pointer hook, so it's set again; the click it missed ran nothing.")]
    private static partial void LogHookRemoved(ILogger logger);
}
