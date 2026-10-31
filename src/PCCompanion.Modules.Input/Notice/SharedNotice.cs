using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.Notice;

/// <summary>
/// The notice window that Lock Keys and Input Language per App share, so a notice from either
/// replaces the other's. It shows a drawn notice where its feature asks, clear of Lock Keys' Caps
/// Lock badge, fades it in and out with Windows' animation effects, and hides it when its time is
/// up. A feature takes it with <see cref="Acquire"/> while it runs. Use it on the host's UI thread.
/// See "The notice" in docs/features/input/input-language-per-app.md.
/// </summary>
public sealed class SharedNotice(TimeProvider time) : IDisposable
{
    private const string ClassName = "PCCompanion.Notice";
    private const int Windows11Build = 22000;

    // The gap between the badge and a notice next to it, in effective pixels.
    private const int BadgeGap = 8;

    private readonly HashSet<object> _users = [];
    private LayeredWindow? _window;
    private ITimer? _timer;
    private Shown? _shown;

    /// <summary>Lock Keys' Caps Lock badge while it shows, which notices at its position keep clear of.</summary>
    public NoticeBadge? Badge { get; set; }

    /// <summary>Whether a feature has taken the notice, so its window is open.</summary>
    public bool IsOpen => _window is not null;

    /// <summary>
    /// The display a notice shows on: the one the window in front is on, where the user types;
    /// with no window in front, the one with the pointer; otherwise the main display.
    /// </summary>
    public static DisplayDetails? ChooseDisplay() =>
        NoticePlacement.ChooseDisplay(Displays.GetDetails(), Displays.AreaOf(Foreground.Window), InputState.PointerPosition);

    /// <summary>
    /// How a notice or the badge looks on <paramref name="display"/>. The Windows mode, high
    /// contrast and the text size are read each time, so a change shows next time.
    /// </summary>
    public static NoticeStyle StyleFor(DisplayDetails display, bool badge)
    {
        ArgumentNullException.ThrowIfNull(display);

        var colors = WindowsTheme.IsHighContrast() ? NoticeColors.HighContrastTheme(WindowsTheme.WindowColor(), WindowsTheme.TextColor())
            : WindowsTheme.IsDark() ? NoticeColors.Dark
            : NoticeColors.Light;
        var build = Environment.OSVersion.Version.Build;
        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        return new NoticeStyle(display.Scale, WindowsTheme.TextScale(), colors, NoticeFonts.For(build), build >= Windows11Build, rightToLeft, badge);
    }

    /// <summary>Whether <paramref name="owner"/>'s notice shows.</summary>
    public bool IsShownBy(object owner) => _shown is { } shown && shown.Owner == owner;

    /// <summary>Takes the notice for a feature that started, creating its window for the first one.</summary>
    public void Acquire(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (_users.Add(owner) && _window is null)
        {
            _window = new LayeredWindow(ClassName, string.Empty, clickThrough: true);
        }
    }

    /// <summary>Hides the feature's notice and gives the notice back, closing its window after the last feature.</summary>
    public void Release(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Hide(owner);
        if (_users.Remove(owner) && _users.Count == 0)
        {
            Dispose();
        }
    }

    /// <summary>
    /// Shows <paramref name="image"/> on <paramref name="display"/> at <paramref name="position"/>,
    /// in place of the notice that shows, whose <c>ended</c> runs first when it's another feature's.
    /// </summary>
    /// <param name="title">The window's name, which screen readers can read: the notice's text.</param>
    /// <param name="fadesOut">Whether it fades out at the end, rather than giving way to the badge at once.</param>
    /// <param name="post">Runs an action later on the UI thread, inside the feature's error boundary, as the fade needs.</param>
    /// <param name="ended">Runs when the notice is hidden because its time is up or another feature's took its place.</param>
    /// <exception cref="InvalidOperationException">The feature hasn't taken the notice.</exception>
    public void Show(object owner, NoticeImage image, DisplayDetails display, NoticePosition position, string title, TimeSpan duration, bool fadesOut, Action<Action> post, Action? ended = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(post);

        if (_window is not { } window || !_users.Contains(owner))
        {
            throw new InvalidOperationException("A feature shows the notice only after it took it.");
        }

        if (IsShownBy(owner))
        {
            Hide(owner);
        }
        else
        {
            End();
        }

        var away = Badge is { } badge && badge.Position == position && display.Bounds.Contains(badge.Bounds)
            ? badge.Bounds.Height + (int)Math.Round(BadgeGap * display.Scale)
            : 0;
        var bounds = NoticePlacement.Place(display, new Size(image.Width, image.Height), position, away);
        var shown = new Shown(owner, time.GetTimestamp(), duration, WindowsTheme.AreAnimationsOn(), fadesOut, ended);
        _shown = shown;

        // Fires on the thread pool; the notice is changed on the UI thread.
        _timer = time.CreateTimer(_ => post(() => Advance(shown)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        window.SetTitle(title);
        window.SetOpacity(shown.Animates ? (byte)0 : byte.MaxValue);
        window.Show(image.Pixels, bounds);
        window.BringToFront();
        Advance(shown);
    }

    /// <summary>Hides the feature's notice if it shows, without running its <c>ended</c>.</summary>
    public void Hide(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (IsShownBy(owner))
        {
            Stop();
        }
    }

    public void Dispose()
    {
        Stop();
        _window?.Dispose();
        _window = null;
    }

    private void Advance(Shown shown)
    {
        if (!ReferenceEquals(_shown, shown) || _window is not { IsShown: true } window)
        {
            return;
        }

        var elapsed = time.GetElapsedTime(shown.ShownAt);
        if (NoticeFade.Opacity(elapsed, shown.Duration, shown.Animates, shown.FadesOut) is not { } opacity)
        {
            End();
            return;
        }

        window.SetOpacity(opacity);
        if (NoticeFade.NextChange(elapsed, shown.Duration, shown.Animates, shown.FadesOut) is { } next)
        {
            _timer?.Change(next, Timeout.InfiniteTimeSpan);
        }
    }

    // Hides the notice that shows, then lets its feature know.
    private void End()
    {
        var ended = _shown?.Ended;
        Stop();
        ended?.Invoke();
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _shown = null;
        _window?.Hide();
    }

    private sealed record Shown(object Owner, long ShownAt, TimeSpan Duration, bool Animates, bool FadesOut, Action? Ended);
}

/// <summary>Where Lock Keys' Caps Lock badge shows, in physical pixels, and at which of the notice's positions.</summary>
public sealed record NoticeBadge(Rectangle Bounds, NoticePosition Position);
