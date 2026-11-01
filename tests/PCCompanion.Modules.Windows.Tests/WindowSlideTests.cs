using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowSlideTests
{
    private const nint Window = 0x100;
    private static readonly Point Above = new(-7, -516);
    private static readonly Point Shown = new(-7, 0);

    private readonly ManualTimeProvider _clock = new();
    private readonly Queue<Action> _thread = new();
    private readonly FakeSlideSystem _system;
    private SlideResult? _result;

    public WindowSlideTests()
    {
        _system = new FakeSlideSystem(_clock) { Location = Above };
    }

    [Fact]
    public void TheSlideStartsQuicklyAndSlowsAtTheEnd()
    {
        var quarter = WindowSlide.At(Above, Shown, WindowSlide.Duration / 4, slidingOut: false);
        var threeQuarters = WindowSlide.At(Above, Shown, WindowSlide.Duration * 3 / 4, slidingOut: false);

        // More than half the way in the first quarter, and less than a tenth in the last.
        Assert.True(quarter.Y - Above.Y > (Shown.Y - Above.Y) / 2, $"{quarter}");
        Assert.True(Shown.Y - threeQuarters.Y < (Shown.Y - Above.Y) / 10, $"{threeQuarters}");
        Assert.Equal(Above, WindowSlide.At(Above, Shown, TimeSpan.Zero, slidingOut: false));
        Assert.Equal(Shown, WindowSlide.At(Above, Shown, WindowSlide.Duration, slidingOut: false));
    }

    [Fact]
    public void TheSlideOutIsTheSlideInBackwards()
    {
        foreach (var milliseconds in new[] { 0, 30, 75, 120, 150 })
        {
            var elapsed = TimeSpan.FromMilliseconds(milliseconds);

            Assert.Equal(
                WindowSlide.At(Above, Shown, WindowSlide.Duration - elapsed, slidingOut: false),
                WindowSlide.At(Shown, Above, elapsed, slidingOut: true));
        }
    }

    [Fact]
    public void EachFrameGetsOneMoveAndTheLastEndsTheSlideOnTime()
    {
        var slide = Slide(Above, Shown);

        slide.Start();
        RunThread();

        Assert.False(slide.IsRunning);
        Assert.Equal(Shown, _system.Moves[^1]);
        Assert.Equal(_system.Moves.Count, _system.Moves.Distinct().Count());
        Assert.Equal(_system.Moves.OrderBy(move => move.Y), _system.Moves);

        // Frames of about 16 ms over 150 ms; the last frames may have no move left to make.
        var result = _result!.Value;
        Assert.InRange(result.Frames, 9, 11);
        Assert.InRange(result.Moves, result.Frames - 2, result.Frames);
        Assert.Equal(result.Moves, _system.Moves.Count);
        Assert.Equal(0, result.Skipped);
        Assert.InRange(result.Time.TotalMilliseconds, 150, 170);
    }

    [Fact]
    public void AWindowThatHasntReachedTheLastMoveGetsNoNewOneForUpToTwoFrames()
    {
        // The app takes 80 ms to make each move, as in the spike.
        _system.MoveTime = TimeSpan.FromMilliseconds(80);
        var slide = Slide(Above, Shown);

        slide.Start();
        RunThread();

        var result = _result!.Value;
        Assert.True(result.Skipped > 0);
        Assert.True(result.Moves < result.Frames);
        Assert.InRange(result.Time.TotalMilliseconds, 150, 170);
        Assert.Equal(Shown, _system.Moves[^1]);

        // Never more than two frames in a row without a move.
        Assert.DoesNotContain(_system.FramesBetweenMoves, frames => frames > WindowSlide.MaximumSkippedFrames + 1);
    }

    [Fact]
    public void AWindowItsAppKeepsElsewhereStillEndsOnTime()
    {
        _system.Stuck = new Point(-7, 200);
        var slide = Slide(Above, Shown);

        slide.Start();
        RunThread();

        Assert.InRange(_result!.Value.Time.TotalMilliseconds, 150, 170);
        Assert.Equal(Shown, _system.Moves[^1]);
    }

    [Fact]
    public void FinishingMovesTheWindowToTheEndAtOnce()
    {
        var slide = Slide(Above, Shown);
        slide.Start();
        RunSteps(2);

        slide.Finish();
        RunThread();

        Assert.False(slide.IsRunning);
        Assert.Equal(Shown, _system.Moves[^1]);
        Assert.NotNull(_result);
    }

    [Fact]
    public void AStoppedSlideMakesNoMoreMovesAndDoesntFinish()
    {
        var slide = Slide(Above, Shown);
        slide.Start();
        RunSteps(2);
        var moves = _system.Moves.Count;

        slide.Stop();
        RunThread();

        Assert.Equal(moves, _system.Moves.Count);
        Assert.Null(_result);
    }

    private WindowSlide Slide(Point from, Point to) =>
        new(Window, from, to, slidingOut: false, _system, _clock, _thread.Enqueue, result => _result = result);

    private void RunThread()
    {
        while (_thread.TryDequeue(out var step))
        {
            step();
        }
    }

    private void RunSteps(int count)
    {
        for (var step = 0; step < count && _thread.TryDequeue(out var action); step++)
        {
            action();
        }
    }

    // Frames of 16 ms; each move reaches the window after MoveTime, or never for a window its app keeps elsewhere.
    private sealed class FakeSlideSystem(ManualTimeProvider clock) : ISlideSystem
    {
        private readonly List<(Point To, DateTimeOffset Lands)> _pending = [];
        private int _framesSinceMove;

        public Point Location { get; set; }

        public Point? Stuck { get; set; }

        public TimeSpan MoveTime { get; set; }

        public List<Point> Moves { get; } = [];

        public List<int> FramesBetweenMoves { get; } = [];

        public void WaitForFrame()
        {
            clock.Advance(TimeSpan.FromMilliseconds(16));
            _framesSinceMove++;
            foreach (var landed in _pending.Where(move => move.Lands <= clock.GetUtcNow()).ToList())
            {
                Location = landed.To;
                _pending.Remove(landed);
            }
        }

        Point? ISlideSystem.Location(nint window) => Stuck ?? Location;

        public bool MoveTo(nint window, Point location)
        {
            Moves.Add(location);
            FramesBetweenMoves.Add(_framesSinceMove);
            _framesSinceMove = 0;
            if (MoveTime == TimeSpan.Zero)
            {
                Location = location;
            }
            else
            {
                _pending.Add((location, clock.GetUtcNow() + MoveTime));
            }

            return true;
        }
    }
}
