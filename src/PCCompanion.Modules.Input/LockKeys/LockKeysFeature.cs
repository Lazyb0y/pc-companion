using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.FullScreen;
using PCCompanion.Modules.Input.Notice;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>
/// Shows a short notice when Caps Lock, Num Lock or Scroll Lock turns on or off. Everything runs on
/// the host's UI thread. See docs/features/input/lock-keys.md.
/// </summary>
/// <param name="notice">The notice window Lock Keys shares with Input Language per App.</param>
public sealed partial class LockKeysFeature(ILogger<LockKeysFeature> logger, SharedNotice notice) : IFeature
{
    private const string WindowClassName = "PCCompanion.LockKeys";
    private const string BadgeClassName = "PCCompanion.LockKeys.Badge";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan CheckTolerance = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan BadgeCheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BadgeCheckTolerance = TimeSpan.FromMilliseconds(500);

    private readonly LockKeyDetector _detector = new();
    private readonly WindowsFullScreenEnvironment _fullScreen = new();
    private LockKeysSettings _settings = new();
    private MessageWindow? _window;
    private CoalescableTimer? _check;
    private SessionMonitor? _sessions;
    private DisplayStateMonitor? _displayState;
    private WinEventHook? _foreground;
    private WinEventHook? _desktops;
    private LayeredWindow? _badge;
    private CoalescableTimer? _badgeCheck;
    private Away _away;
    private bool _failed;

    // Whether Lock Keys' notice that shows says Caps Lock turned on.
    private bool _noticeShowsCapsLockOn;

    // The badge that shows, which isn't drawn again while nothing it depends on changed.
    private BadgeLook? _badgeLook;
    private Rectangle _badgeBounds;

    [Flags]
    private enum Away
    {
        None = 0,
        Locked = 1,
        OtherDesktop = 2,
        DisplaysOff = 4,
    }

    public static FeatureInfo Definition { get; } = new("lockKeys", InputModule.Id, "LockKeysName", "LockKeysDescription", "");

    /// <summary>
    /// Whether this PC can run Lock Keys, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _window is not null;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        LockKeysSettings read;
        try
        {
            read = LockKeysSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            read = new LockKeysSettings();
        }

        // Saving other settings, such as another feature's, leaves a running Lock Keys as it is.
        var previous = _settings;
        var unchanged = _window is not null && read == previous;
        _settings = read;
        if (unchanged)
        {
            return;
        }

        if (!_settings.Enabled)
        {
            Stop();
            return;
        }

        var starting = _window is null;
        if (starting)
        {
            Start();
        }

        _detector.Configure(_settings);
        LogConfigured(logger, _settings.CapsLock, _settings.NumLock, _settings.ScrollLock, _settings.Position, _settings.DurationMilliseconds);

        // A change of how the notice shows shows it once, so the user sees the result.
        if (!starting && (read.Position != previous.Position || read.DurationMilliseconds != previous.DurationMilliseconds || read.KeepCapsLockBadge != previous.KeepCapsLockBadge))
        {
            ShowSample();
        }
        else
        {
            UpdateBadge();
        }
    }

    public void Dispose() => Stop();

    private static LockKeyState ReadState() =>
        new(InputState.IsLockKeyOn(LockKey.CapsLock), InputState.IsLockKeyOn(LockKey.NumLock), InputState.IsLockKeyOn(LockKey.ScrollLock));

    private static string TextOf(LockKeyChange change) => (change.Key, change.IsOn) switch
    {
        (LockKey.CapsLock, true) => Strings.LockKeysCapsLockOn,
        (LockKey.CapsLock, false) => Strings.LockKeysCapsLockOff,
        (LockKey.NumLock, true) => Strings.LockKeysNumLockOn,
        (LockKey.NumLock, false) => Strings.LockKeysNumLockOff,
        (LockKey.ScrollLock, true) => Strings.LockKeysScrollLockOn,
        _ => Strings.LockKeysScrollLockOff,
    };

    private void Start()
    {
        var window = new MessageWindow(WindowClassName);
        try
        {
            _sessions = new SessionMonitor(window);
            _displayState = new DisplayStateMonitor(window);
            _foreground = new WinEventHook(WinEventHook.ForegroundEvent, WinEventHook.ForegroundEvent, skipOwnProcess: true);
            _desktops = new WinEventHook(WinEventHook.DesktopSwitchEvent, WinEventHook.DesktopSwitchEvent);
            notice.Acquire(this);
            _badge = new LayeredWindow(BadgeClassName, Strings.LockKeysBadge, clickThrough: true);
        }
        catch
        {
            DisposeParts();
            window.Dispose();
            throw;
        }

        _window = window;
        _window.MessageReceived += OnMessageReceived;
        _sessions.Locked += (_, _) => Guard(() => GoAway(Away.Locked));
        _sessions.Unlocked += (_, _) => Guard(() => ComeBack(Away.Locked));
        _displayState.TurnedOff += (_, _) => Guard(() => GoAway(Away.DisplaysOff));
        _displayState.TurnedOn += (_, _) => Guard(() => ComeBack(Away.DisplaysOff));
        _desktops.Raised += (_, _) => Guard(OnDesktopSwitched);
        _foreground.Raised += (_, _) => Guard(UpdateBadge);

        _away = InputDesktop.IsUsersOwn() ? Away.None : Away.OtherDesktop;
        _detector.Reset(ReadState());
        if (_away == Away.None)
        {
            StartChecking();
        }

        WarmUp();
        LogStarted(logger);
    }

    // The first drawing loads the fonts and the text shaping, which took 50 ms in Windows Sandbox,
    // so it's done once on the thread pool, in the user's language, before any notice shows.
    private void WarmUp()
    {
        var style = SharedNotice.StyleFor(new DisplayDetails(Rectangle.Empty, Rectangle.Empty, 96), badge: false);
        NoticeLine[] notice = [new(Strings.LockKeysCapsLockOn, NoticeIcons.Lock)];
        NoticeLine[] badge = [new(Strings.LockKeysBadge, NoticeIcons.Lock)];
        _ = Task.Run(() =>
        {
            try
            {
                NoticePainter.Paint(notice, style);
                NoticePainter.Paint(badge, style with { Badge = true });
            }
            catch (Win32Exception exception)
            {
                LogWarmUpFailed(logger, exception);
            }
        });
    }

    private void Stop()
    {
        if (_window is null)
        {
            return;
        }

        _window.MessageReceived -= OnMessageReceived;
        DisposeParts();
        _window.Dispose();
        _window = null;
        _away = Away.None;
        _badgeLook = null;
        LogStopped(logger);
    }

    private void DisposeParts()
    {
        notice.Release(this);
        notice.Badge = null;
        _check?.Dispose();
        _badgeCheck?.Dispose();
        _foreground?.Dispose();
        _desktops?.Dispose();
        _displayState?.Dispose();
        _sessions?.Dispose();
        _badge?.Dispose();
        (_check, _badgeCheck, _foreground, _desktops, _displayState, _sessions, _badge) = (null, null, null, null, null, null, null);
    }

    private void StartChecking()
    {
        if (_window is null || _check is not null)
        {
            return;
        }

        _check = new CoalescableTimer(_window, CheckInterval, CheckTolerance);
        _check.Elapsed += (_, _) => Guard(Check);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (Displays.IsChangeMessage(e.Message))
        {
            Guard(() =>
            {
                HideNotice();
                _badgeLook = null;
                UpdateBadge();
            });
        }
    }

    // The secure desktop, of the sign-in screen or a UAC prompt, shows nothing of this desktop.
    private void OnDesktopSwitched()
    {
        if (InputDesktop.IsUsersOwn())
        {
            ComeBack(Away.OtherDesktop);
        }
        else
        {
            GoAway(Away.OtherDesktop);
        }
    }

    // While the user is away, nothing is checked or shown.
    private void GoAway(Away reason)
    {
        var wasAway = _away != Away.None;
        _away |= reason;
        if (wasAway)
        {
            return;
        }

        _check?.Dispose();
        _check = null;
        HideNotice();
        HideBadge();
        LogPaused(logger, reason);
    }

    // A change made while away, such as on the lock screen, is taken without a notice.
    private void ComeBack(Away reason)
    {
        if ((_away & reason) == 0)
        {
            return;
        }

        _away &= ~reason;
        if (_away != Away.None)
        {
            return;
        }

        _detector.Reset(ReadState());
        StartChecking();
        LogReadAgain(logger, reason);
        UpdateBadge();
    }

    private void Check()
    {
        if (_window is null || _away != Away.None)
        {
            return;
        }

        var changes = _detector.Check(ReadState());
        if (changes.Count > 0)
        {
            ShowNotice([.. changes]);
        }
    }

    // After a change of how the notice shows: Caps Lock's state, whatever the keys chosen.
    private void ShowSample()
    {
        if (_away != Away.None)
        {
            return;
        }

        ShowNotice([new LockKeyChange(LockKey.CapsLock, InputState.IsLockKeyOn(LockKey.CapsLock))]);
        LogSampleShown(logger);
    }

    private void ShowNotice(LockKeyChange[] changes)
    {
        if (_window is null || SharedNotice.ChooseDisplay() is not { } display)
        {
            return;
        }

        if (IsFullScreen(display))
        {
            foreach (var change in changes)
            {
                LogSkipped(logger, change.Key, change.IsOn);
            }

            HideNotice();
            UpdateBadge();
            return;
        }

        // Caps Lock turning on shows the notice in place of the badge, which follows it.
        _noticeShowsCapsLockOn = changes.Any(change => change is { Key: LockKey.CapsLock, IsOn: true });
        UpdateBadge();
        var lines = changes.Select(change => new NoticeLine(TextOf(change), change.IsOn ? NoticeIcons.Lock : NoticeIcons.Unlock)).ToArray();
        var image = NoticePainter.Paint(lines, SharedNotice.StyleFor(display, badge: false));
        var title = string.Join(", ", lines.Select(line => line.Text));
        var fadesOut = !(_noticeShowsCapsLockOn && _detector.ShowsBadge);
        notice.Show(this, image, display, _settings.Position, title, TimeSpan.FromMilliseconds(_settings.DurationMilliseconds), fadesOut, Post, NoticeEnded);

        foreach (var change in changes)
        {
            LogShown(logger, change.Key, change.IsOn);
        }
    }

    // The notice's time was up, or Input Language per App's notice took its place.
    private void NoticeEnded()
    {
        _noticeShowsCapsLockOn = false;
        UpdateBadge();
    }

    private void HideNotice()
    {
        notice.Hide(this);
        _noticeShowsCapsLockOn = false;
    }

    // Runs an action later on the window's thread, inside the feature's error boundary.
    private void Post(Action action) => _ = _window?.BeginInvoke(() => Guard(action));

    // Shows, moves or hides the badge: it shows while Caps Lock is on, unless the user is away, the
    // notice for Caps Lock turning on shows in its place, or an app is full screen on its display.
    private void UpdateBadge()
    {
        if (_badge is null || _window is null)
        {
            return;
        }

        if (!_detector.ShowsBadge || _away != Away.None || (_noticeShowsCapsLockOn && notice.IsShownBy(this)) || SharedNotice.ChooseDisplay() is not { } display || IsFullScreen(display))
        {
            HideBadge();
            return;
        }

        var style = SharedNotice.StyleFor(display, badge: true);
        var look = new BadgeLook(display, _settings.Position, style);
        if (look != _badgeLook || !_badge.IsShown)
        {
            var image = NoticePainter.Paint([new NoticeLine(Strings.LockKeysBadge, NoticeIcons.Lock)], style);
            _badgeBounds = NoticePlacement.Place(display, new Size(image.Width, image.Height), _settings.Position);
            _badge.Show(image.Pixels, _badgeBounds);
            _badgeLook = look;
        }

        notice.Badge = new NoticeBadge(_badgeBounds, _settings.Position);
        _badge.BringToFront();

        // A window can go full screen without another coming to the front.
        if (_badgeCheck is null)
        {
            _badgeCheck = new CoalescableTimer(_window, BadgeCheckInterval, BadgeCheckTolerance);
            _badgeCheck.Elapsed += (_, _) => Guard(UpdateBadge);
        }
    }

    private void HideBadge()
    {
        _badge?.Hide();
        notice.Badge = null;
        _badgeCheck?.Dispose();
        _badgeCheck = null;
    }

    // Exclusive full screen always counts: a window can't show over it, and trying can take a game
    // out of full screen.
    private bool IsFullScreen(DisplayDetails display) =>
        (_fullScreen.FullScreenMode == FullScreenMode.Exclusive && _fullScreen.FrontWindowDisplay == display.Bounds)
        || (_settings.IgnoreFullScreenApps && FullScreenCheck.IsFullScreen(_fullScreen, display.Bounds));

    // Runs on the window's thread, where an exception would end the host. Like a failing Apply,
    // a failure stops the feature until the host restarts.
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            _failed = true;
            Stop();
        }
    }

    private sealed record BadgeLook(DisplayDetails Display, NoticePosition Position, NoticeStyle Style);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys applied its settings: Caps Lock {CapsLock}, Num Lock {NumLock}, Scroll Lock {ScrollLock}, {Position}, {DurationMilliseconds} ms.")]
    private static partial void LogConfigured(ILogger logger, bool capsLock, bool numLock, bool scrollLock, NoticePosition position, int durationMilliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Key} changed, now on: {On}; the notice showed.")]
    private static partial void LogShown(ILogger logger, LockKey key, bool on);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Key} changed, now on: {On}; no notice, since an app was full screen.")]
    private static partial void LogSkipped(ILogger logger, LockKey key, bool on);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys showed its notice after a settings change.")]
    private static partial void LogSampleShown(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys paused: {Reason}.")]
    private static partial void LogPaused(ILogger logger, Away reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys read the lock keys again without a notice: {Reason} ended.")]
    private static partial void LogReadAgain(ILogger logger, Away reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock Keys couldn't draw its notice ahead of time; the first notice draws it.")]
    private static partial void LogWarmUpFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Lock Keys settings can't be read, so Lock Keys is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lock Keys failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
