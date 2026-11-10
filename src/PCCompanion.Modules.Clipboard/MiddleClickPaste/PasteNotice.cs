using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>The notices' texts and where they go, as "When nothing is pasted" in docs/features/clipboard/middle-click-paste.md says.</summary>
public static class PasteNotices
{
    /// <summary>How far below the pointer the notice starts, in effective pixels, as a tooltip does.</summary>
    public const int Below = 20;

    private static readonly CompositeFormat TooLongFormat = CompositeFormat.Parse(Strings.MiddleClickPasteNoticeTooLong);

    /// <summary>
    /// The notice for <paramref name="outcome"/>, in the user's language, or <see langword="null"/>
    /// for a paste, which needs none.
    /// </summary>
    public static string? TextOf(PasteOutcome outcome) => outcome switch
    {
        PasteOutcome.Pasted => null,
        PasteOutcome.NothingSelected => Strings.MiddleClickPasteNoticeNothingSelected,
        PasteOutcome.NotShared => Strings.MiddleClickPasteNoticeNotShared,
        PasteOutcome.Password => Strings.MiddleClickPasteNoticePassword,
        PasteOutcome.AppRunsAsAdministrator => Strings.MiddleClickPasteNoticeAppAdministrator,
        PasteOutcome.RunsAsAdministrator => Strings.MiddleClickPasteNoticeOwnAdministrator,
        PasteOutcome.AppHasFewerRights => Strings.MiddleClickPasteNoticeFewerRights,
        PasteOutcome.AppNotResponding => Strings.MiddleClickPasteNoticeAppNoAnswer,
        PasteOutcome.TooLong => string.Format(CultureInfo.CurrentCulture, TooLongFormat, MiddleClickPasteController.MaxLength.ToString("N0", CultureInfo.CurrentCulture)),
        PasteOutcome.BoxRefused => Strings.MiddleClickPasteNoticeBoxRefused,
        PasteOutcome.BoxNotResponding => Strings.MiddleClickPasteNoticeBoxNotResponding,
        PasteOutcome.PointerMoved => Strings.MiddleClickPasteNoticePointerMoved,
        PasteOutcome.Busy => Strings.MiddleClickPasteNoticeBusy,
        _ => null,
    };

    /// <summary>
    /// Where a notice of <paramref name="size"/> goes for a click at <paramref name="point"/>:
    /// below the pointer, starting at it, or ending at it in right-to-left languages, and above it
    /// when there isn't room below; always inside the display's work area.
    /// </summary>
    public static Rectangle Place(DisplayDetails display, Size size, Point point, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(display);

        var area = display.WorkArea;
        var gap = (int)Math.Round(Below * display.Scale);
        var x = rightToLeft ? point.X - size.Width : point.X;
        var y = point.Y + gap + size.Height <= area.Bottom ? point.Y + gap : point.Y - gap - size.Height;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Rectangle(x, y, size.Width, size.Height);
    }
}

/// <summary>
/// Shows why a middle-click pasted nothing, next to the pointer, for <see cref="Duration"/> or until
/// the pointer moves away, drawn like Lock Keys' notice. It can't be activated and lets clicks
/// through. Use it on the host's UI thread.
/// </summary>
public sealed class PasteNotice(TimeProvider time) : IDisposable
{
    /// <summary>How long the notice shows.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    // How far the pointer moves away, in effective pixels, before the notice goes.
    private const int AwayDistance = 32;

    private const string ClassName = "PCCompanion.MiddleClickPaste.Notice";

    // Information, which Segoe Fluent Icons and Segoe MDL2 Assets both have.
    private const string InfoIcon = "";

    private LayeredWindow? _window;
    private ITimer? _timer;
    private Shown? _shown;

    /// <summary>Whether a notice shows now.</summary>
    public bool IsShown => _shown is not null;

    /// <summary>Shows <paramref name="text"/> for a click at <paramref name="point"/>, in place of any notice that shows.</summary>
    /// <param name="post">Runs an action later on the UI thread, inside the feature's error boundary, as the fade needs.</param>
    public void Show(string text, Point point, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(post);

        Stop();
        if (Displays.GetDetails().FirstOrDefault(display => display.Bounds.Contains(point)) is not { } display)
        {
            return;
        }

        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        var image = NoticePainter.Paint([new NoticeLine(text, InfoIcon)], NoticeStyles.For(display, rightToLeft));
        var bounds = PasteNotices.Place(display, new Size(image.Width, image.Height), point, rightToLeft);
        var window = _window ??= new LayeredWindow(ClassName, string.Empty, clickThrough: true);
        var shown = new Shown(point, display.Scale, time.GetTimestamp(), WindowsTheme.AreAnimationsOn());
        _shown = shown;

        // Fires on the thread pool; the notice is changed on the UI thread.
        _timer = time.CreateTimer(_ => post(() => Advance(shown)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        window.SetTitle(text);
        window.SetOpacity(shown.Animates ? (byte)0 : byte.MaxValue);
        window.Show(image.Pixels, bounds);
        window.BringToFront();
        Advance(shown);
    }

    /// <summary>Hides the notice once the pointer has moved away from where it was shown.</summary>
    public void PointerMoved(Point point)
    {
        if (_shown is { } shown)
        {
            var away = AwayDistance * shown.Scale;
            if (Math.Abs(point.X - shown.Point.X) > away || Math.Abs(point.Y - shown.Point.Y) > away)
            {
                Stop();
            }
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
        if (NoticeFade.Opacity(elapsed, Duration, shown.Animates) is not { } opacity)
        {
            Stop();
            return;
        }

        window.SetOpacity(opacity);
        if (NoticeFade.NextChange(elapsed, Duration, shown.Animates) is { } next)
        {
            _timer?.Change(next, Timeout.InfiniteTimeSpan);
        }
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _shown = null;
        _window?.Hide();
    }

    private sealed record Shown(Point Point, double Scale, long ShownAt, bool Animates);
}
