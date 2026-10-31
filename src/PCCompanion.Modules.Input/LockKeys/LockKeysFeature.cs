using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
public sealed partial class LockKeysFeature(ILogger<LockKeysFeature> logger, TimeProvider time) : IFeature
{
    private const string WindowClassName = "PCCompanion.LockKeys";
    private const string NoticeClassName = "PCCompanion.LockKeys.Notice";
    private const string BadgeClassName = "PCCompanion.LockKeys.Badge";
    private const int Windows11Build = 22000;

    // The gap between the badge and a notice next to it, in effective pixels.
    private const int BadgeGap = 8;

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
    private LayeredWindow? _notice;
    private LayeredWindow? _badge;
    private ITimer? _noticeTimer;
    private CoalescableTimer? _badgeCheck;
    private Away _away;
    private bool _failed;

    // The notice that shows: when, whether it fades, and whether it says Caps Lock turned on.
    private long _noticeShownAt;
    private bool _noticeAnimates;
    private bool _noticeFadesOut;
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
            _notice = new LayeredWindow(NoticeClassName, Strings.LockKeysName, clickThrough: true);
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

        // Fires on the thread pool; the notice is changed on the window's thread.
        _noticeTimer = time.CreateTimer(_ => window.BeginInvoke(() => Guard(AdvanceNotice)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

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
        var style = StyleFor(new DisplayDetails(Rectangle.Empty, Rectangle.Empty, 96), badge: false);
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
        _noticeTimer?.Dispose();
        _check?.Dispose();
        _badgeCheck?.Dispose();
        _foreground?.Dispose();
        _desktops?.Dispose();
        _displayState?.Dispose();
        _sessions?.Dispose();
        _notice?.Dispose();
        _badge?.Dispose();
        (_noticeTimer, _check, _badgeCheck, _foreground, _desktops, _displayState, _sessions, _notice, _badge) = (null, null, null, null, null, null, null, null, null);
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
        if (_notice is null || ChooseDisplay() is not { } display)
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
        var image = NoticePainter.Paint(lines, StyleFor(display, badge: false));
        var away = _badge is { IsShown: true } && display.Bounds.Contains(_badgeBounds) ? _badgeBounds.Height + (int)Math.Round(BadgeGap * display.Scale) : 0;
        var bounds = NoticePlacement.Place(display, new Size(image.Width, image.Height), _settings.Position, away);

        _noticeAnimates = WindowsTheme.AreAnimationsOn();
        _noticeFadesOut = !(_noticeShowsCapsLockOn && _detector.ShowsBadge);
        _noticeShownAt = time.GetTimestamp();
        _notice.SetTitle(string.Join(", ", lines.Select(line => line.Text)));
        _notice.SetOpacity(_noticeAnimates ? (byte)0 : byte.MaxValue);
        _notice.Show(image.Pixels, bounds);
        _notice.BringToFront();
        AdvanceNotice();

        foreach (var change in changes)
        {
            LogShown(logger, change.Key, change.IsOn);
        }
    }

    private void AdvanceNotice()
    {
        if (_notice is not { IsShown: true })
        {
            return;
        }

        var elapsed = time.GetElapsedTime(_noticeShownAt);
        var duration = TimeSpan.FromMilliseconds(_settings.DurationMilliseconds);
        if (NoticeFade.Opacity(elapsed, duration, _noticeAnimates, _noticeFadesOut) is not { } opacity)
        {
            HideNotice();
            UpdateBadge();
            return;
        }

        _notice.SetOpacity(opacity);
        if (NoticeFade.NextChange(elapsed, duration, _noticeAnimates, _noticeFadesOut) is { } next)
        {
            _noticeTimer?.Change(next, Timeout.InfiniteTimeSpan);
        }
    }

    private void HideNotice()
    {
        _noticeTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _notice?.Hide();
        _noticeShowsCapsLockOn = false;
    }

    // Shows, moves or hides the badge: it shows while Caps Lock is on, unless the user is away, the
    // notice for Caps Lock turning on shows in its place, or an app is full screen on its display.
    private void UpdateBadge()
    {
        if (_badge is null || _window is null)
        {
            return;
        }

        if (!_detector.ShowsBadge || _away != Away.None || (_noticeShowsCapsLockOn && _notice is { IsShown: true }) || ChooseDisplay() is not { } display || IsFullScreen(display))
        {
            HideBadge();
            return;
        }

        var style = StyleFor(display, badge: true);
        var look = new BadgeLook(display, _settings.Position, style);
        if (look != _badgeLook || !_badge.IsShown)
        {
            var image = NoticePainter.Paint([new NoticeLine(Strings.LockKeysBadge, NoticeIcons.Lock)], style);
            _badgeBounds = NoticePlacement.Place(display, new Size(image.Width, image.Height), _settings.Position);
            _badge.Show(image.Pixels, _badgeBounds);
            _badgeLook = look;
        }

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
        _badgeCheck?.Dispose();
        _badgeCheck = null;
    }

    private static DisplayDetails? ChooseDisplay() =>
        NoticePlacement.ChooseDisplay(Displays.GetDetails(), Displays.AreaOf(Foreground.Window), InputState.PointerPosition);

    // Exclusive full screen always counts: a window can't show over it, and trying can take a game
    // out of full screen.
    private bool IsFullScreen(DisplayDetails display) =>
        (_fullScreen.FullScreenMode == FullScreenMode.Exclusive && _fullScreen.FrontWindowDisplay == display.Bounds)
        || (_settings.IgnoreFullScreenApps && FullScreenCheck.IsFullScreen(_fullScreen, display.Bounds));

    // The Windows mode, high contrast and the text size are read each time, so a change shows next time.
    private static NoticeStyle StyleFor(DisplayDetails display, bool badge)
    {
        var colors = WindowsTheme.IsHighContrast() ? NoticeColors.HighContrastTheme(WindowsTheme.WindowColor(), WindowsTheme.TextColor())
            : WindowsTheme.IsDark() ? NoticeColors.Dark
            : NoticeColors.Light;
        var build = Environment.OSVersion.Version.Build;
        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        return new NoticeStyle(display.Scale, WindowsTheme.TextScale(), colors, NoticeFonts.For(build), build >= Windows11Build, rightToLeft, badge);
    }

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
