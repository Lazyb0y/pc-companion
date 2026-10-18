using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Decides when a corner triggers. Call <see cref="Update"/> on every pointer event and again
/// when a wait it asked for has passed. All calls must come from one thread.
/// </summary>
public sealed class CornerDetector(IHotCornerEnvironment environment, TimeProvider time)
{
    private HotCornersSettings _settings = new();
    private Corner[] _corners = [];
    private Corner? _current;
    private long _enteredAt;
    private bool _finished;

    /// <summary>
    /// The corner the pointer is in, among the corners that have an action, as of the last call to
    /// <see cref="Configure"/> or <see cref="Update"/>; <see langword="null"/> when it's in none.
    /// </summary>
    public ScreenCorner? CurrentCorner => _current?.Position;

    /// <summary>
    /// Sets the settings and the areas of the displays, in physical pixels. A corner the pointer
    /// is in already doesn't trigger until the pointer leaves it and comes back.
    /// </summary>
    public void Configure(HotCornersSettings settings, IReadOnlyList<Rectangle> displays)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(displays);

        _settings = settings;
        _corners = [.. displays
            .SelectMany(display => CornersOf(display, displays))
            .Where(corner => settings.For(corner.Position).Action != HotCornerAction.None)];
        _current = Find(environment.PointerPosition);
        _finished = true;
    }

    public CornerDecision Update()
    {
        var corner = Find(environment.PointerPosition);
        if (corner != _current)
        {
            _current = corner;
            _enteredAt = time.GetTimestamp();
            _finished = false;

            // Dragging a window into a corner snaps it there; releasing the button before the
            // delay ends must not trigger the corner.
            if (corner is { } entered && environment.IsMouseButtonHeld)
            {
                return Finish(entered, CornerOutcome.MouseButtonHeld);
            }
        }

        if (_current is not { } current || _finished)
        {
            return CornerDecision.Nothing;
        }

        var delay = TimeSpan.FromMilliseconds(_settings.ActivationDelayMilliseconds);
        var elapsed = time.GetElapsedTime(_enteredAt);
        if (elapsed < delay)
        {
            return new CornerDecision(CornerOutcome.Waiting, current.Position, _settings.For(current.Position).Action, delay - elapsed);
        }

        var key = _settings.For(current.Position).Key;
        return Finish(
            current,
            environment.IsMouseButtonHeld ? CornerOutcome.MouseButtonHeld
            : key != HotCornerKey.None && !environment.IsKeyHeld(key) ? CornerOutcome.KeyNotHeld
            : _settings.IgnoreFullScreenApps && environment.IsFullScreenAppInFront ? CornerOutcome.FullScreenApp
            : CornerOutcome.Triggered);
    }

    private CornerDecision Finish(Corner corner, CornerOutcome outcome)
    {
        _finished = true;
        return new CornerDecision(outcome, corner.Position, _settings.For(corner.Position).Action, TimeSpan.Zero);
    }

    private Corner? Find(Point? position)
    {
        foreach (var corner in _corners)
        {
            if (corner.Point == position)
            {
                return corner;
            }
        }

        return null;
    }

    // A corner that touches another display, even diagonally, isn't one: the pointer passes into
    // the other display there instead of stopping.
    private static IEnumerable<Corner> CornersOf(Rectangle display, IReadOnlyList<Rectangle> displays)
    {
        if (display.Width <= 0 || display.Height <= 0)
        {
            yield break;
        }

        (ScreenCorner Position, int X, int Y, int OutwardX, int OutwardY)[] corners =
        [
            (ScreenCorner.TopLeft, display.Left, display.Top, -1, -1),
            (ScreenCorner.TopRight, display.Right - 1, display.Top, 1, -1),
            (ScreenCorner.BottomLeft, display.Left, display.Bottom - 1, -1, 1),
            (ScreenCorner.BottomRight, display.Right - 1, display.Bottom - 1, 1, 1),
        ];

        foreach (var (position, x, y, outwardX, outwardY) in corners)
        {
            var touchesAnotherDisplay = displays.Any(other =>
                other.Contains(x + outwardX, y) || other.Contains(x, y + outwardY) || other.Contains(x + outwardX, y + outwardY));

            if (!touchesAnotherDisplay)
            {
                yield return new Corner(new Point(x, y), position);
            }
        }
    }

    private readonly record struct Corner(Point Point, ScreenCorner Position);
}
