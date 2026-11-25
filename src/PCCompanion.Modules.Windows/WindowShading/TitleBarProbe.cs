using System;
using System.Drawing;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// Finds where a window's title bar ends: asks the window, without waiting for it, what's at
/// points from the top of its frame downwards, 2 pixels apart, at 40% and 60% of its width, as
/// Title Bar Actions asks, through the child window under each point. Runs on the feature's thread,
/// where the answers come.
/// </summary>
internal sealed class TitleBarProbe
{
    // How long the window has to answer.
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(150);

    private readonly nint _window;
    private readonly Rectangle _frame;
    private readonly int _band;
    private readonly Action<Action> _post;
    private readonly Action<int?> _measured;
    private readonly Column[] _columns;
    private bool _done;

    private TitleBarProbe(nint window, Rectangle frame, int band, Action<Action> post, Action<int?> measured)
    {
        _window = window;
        _frame = frame;
        _band = band;
        _post = post;
        _measured = measured;
        _columns = [new Column(frame.Left + (int)(frame.Width * 0.4)), new Column(frame.Left + (int)(frame.Width * 0.6))];
    }

    /// <summary>
    /// Starts asking; <paramref name="measured"/> gets, once, on the feature's thread, the height of
    /// the title bar from the frame's top, at most <paramref name="band"/>, or null when the
    /// window didn't say in time.
    /// </summary>
    /// <param name="post">Runs work on the feature's thread, inside its error boundary.</param>
    public static void Measure(nint window, Rectangle frame, int band, Action<Action> post, Action<int?> measured)
    {
        // The timer is stopped as the probe finishes, whichever comes first.
        Timer? timer = null;
        var probe = new TitleBarProbe(window, frame, band, post, height =>
        {
            timer?.Dispose();
            measured(height);
        });
        timer = new Timer(_ => post(probe.Finish), null, Timeout, System.Threading.Timeout.InfiniteTimeSpan);
        foreach (var column in probe._columns)
        {
            probe.Ask(column, frame.Top + 1);
        }
    }

    // The window under the point is asked, as Windows asks it; another window there, such as one
    // on top, ends the column.
    private void Ask(Column column, int y)
    {
        var point = new Point(column.X, y);
        if (_done || y >= _frame.Top + _band || y >= _frame.Bottom)
        {
            Done(column);
            return;
        }

        var under = WindowHitTest.WindowAt(point);
        if (under == 0 || WindowHitTest.Root(under) != _window)
        {
            Done(column);
            return;
        }

        AskWindow(column, under, point);
    }

    private void AskWindow(Column column, nint window, Point point)
    {
        if (!WindowHitTest.Request(window, point, answer => _post(() => Answered(column, window, point, answer))))
        {
            Done(column);
        }
    }

    private void Answered(Column column, nint window, Point point, HitTestAnswer answer)
    {
        if (_done)
        {
            return;
        }

        // A child that answers HTTRANSPARENT passes the question to its parent.
        if (answer == HitTestAnswer.Transparent && WindowHitTest.Parent(window) is var parent and not 0)
        {
            AskWindow(column, parent, point);
            return;
        }

        if (answer == HitTestAnswer.Caption)
        {
            column.LastCaption = point.Y;
        }
        else if (column.LastCaption is not null)
        {
            Done(column);
            return;
        }

        Ask(column, point.Y + 2);
    }

    private void Done(Column column)
    {
        column.IsDone = true;
        if (Array.TrueForAll(_columns, each => each.IsDone))
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        var lowest = 0;
        foreach (var column in _columns)
        {
            if (column.LastCaption is { } last)
            {
                lowest = Math.Max(lowest, last - _frame.Top + 1);
            }
        }

        _measured(lowest > 0 ? Math.Min(lowest, _band) : null);
    }

    private sealed class Column(int x)
    {
        public int X { get; } = x;

        public int? LastCaption { get; set; }

        public bool IsDone { get; set; }
    }
}
