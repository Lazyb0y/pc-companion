using System;
using System.Drawing;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>What a <see cref="WindowSlide"/> needs from Windows, so tests can supply it instead.</summary>
public interface ISlideSystem
{
    /// <summary>Waits until the compositor has drawn its next frame, at most about one frame's time.</summary>
    void WaitForFrame();

    /// <summary>The window's top-left corner, invisible borders included, or <see langword="null"/> when it has closed.</summary>
    Point? Location(nint window);

    /// <summary>Moves the window, keeping its size, without waiting for its program. Returns false when Windows refused.</summary>
    bool MoveTo(nint window, Point location);
}

/// <summary>How a slide went, for the log.</summary>
/// <param name="Frames">The frames it waited for.</param>
/// <param name="Moves">The moves it sent.</param>
/// <param name="Skipped">The frames it sent no move in, since the window hadn't reached the last one.</param>
/// <param name="Time">From its start to its last move.</param>
public readonly record struct SlideResult(int Frames, int Moves, int Skipped, TimeSpan Time);

/// <summary>
/// Slides a window in or out, as "Sliding in" in docs/features/windows/drop-down-apps.md says: one
/// move per frame Windows draws, quickly at first and slowing at the end, over about 150 ms. While
/// the window hasn't reached the last move, as for an app that's slow to move it, no new move is
/// sent, for up to two frames. The slide always ends on time, at its end. Use it on
/// the feature's thread: each frame runs in a step that <c>post</c> queues behind the thread's
/// other messages.
/// </summary>
public sealed class WindowSlide
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    /// <summary>How many frames in a row a move may be held back for a window that hasn't reached the last one.</summary>
    public const int MaximumSkippedFrames = 2;

    private readonly nint _window;
    private readonly Point _from;
    private readonly Point _to;
    private readonly bool _out;
    private readonly ISlideSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly Action<SlideResult> _finished;
    private long _started;
    private Point? _sent;
    private int _frames;
    private int _moves;
    private int _skipped;
    private int _skippedInARow;

    /// <param name="from">Where the window is when the slide starts.</param>
    /// <param name="to">Where the slide ends.</param>
    /// <param name="slidingOut">
    /// Whether it slides out: the slide in, backwards, so it starts slowly and speeds up.
    /// </param>
    /// <param name="post">Queues an action on the feature's thread, behind its other messages.</param>
    /// <param name="finished">Runs on the feature's thread once the last move was sent, unless the slide was stopped.</param>
    public WindowSlide(nint window, Point from, Point to, bool slidingOut, ISlideSystem system, TimeProvider time, Action<Action> post, Action<SlideResult> finished)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(finished);

        _window = window;
        _from = from;
        _to = to;
        _out = slidingOut;
        _system = system;
        _time = time;
        _post = post;
        _finished = finished;
    }

    public nint Window => _window;

    /// <summary>Whether the slide still runs.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Where the window is after <paramref name="elapsed"/>, given where it starts and ends.</summary>
    public static Point At(Point from, Point to, TimeSpan elapsed, bool slidingOut)
    {
        var progress = Math.Clamp(elapsed / Duration, 0, 1);

        // The slide out is the slide in backwards.
        var eased = slidingOut ? 1 - Ease(1 - progress) : Ease(progress);
        return new Point(
            from.X + (int)Math.Round((to.X - from.X) * eased),
            from.Y + (int)Math.Round((to.Y - from.Y) * eased));
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _started = _time.GetTimestamp();
        _sent = _from;
        _post(Step);
    }

    /// <summary>Ends the slide at once with the move to its end, as when its shortcut is pressed again meanwhile.</summary>
    public void Finish()
    {
        if (IsRunning)
        {
            End();
        }
    }

    /// <summary>Stops the slide where it is, without its last move, as when the window closed.</summary>
    public void Stop() => IsRunning = false;

    // Quickly at first, slowing at the end.
    private static double Ease(double progress) => 1 - Math.Pow(1 - progress, 3);

    private void Step()
    {
        if (!IsRunning)
        {
            return;
        }

        _system.WaitForFrame();
        if (!IsRunning)
        {
            return;
        }

        _frames++;
        var elapsed = _time.GetElapsedTime(_started);
        if (elapsed >= Duration)
        {
            End();
            return;
        }

        // A window that hasn't reached the last move gets no new one, for up to two frames.
        if (_sent is { } sent && _system.Location(_window) != sent && _skippedInARow < MaximumSkippedFrames)
        {
            _skippedInARow++;
            _skipped++;
            _post(Step);
            return;
        }

        _skippedInARow = 0;
        var next = At(_from, _to, elapsed, _out);
        if (next != _sent)
        {
            _system.MoveTo(_window, next);
            _sent = next;
            _moves++;
        }

        _post(Step);
    }

    private void End()
    {
        IsRunning = false;
        if (_sent != _to)
        {
            _system.MoveTo(_window, _to);
            _sent = _to;
            _moves++;
        }

        _finished(new SlideResult(_frames, _moves, _skipped, _time.GetElapsedTime(_started)));
    }
}
