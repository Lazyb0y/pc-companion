using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Decides when a corner or edge triggers, and where the wheel has an action. Call
/// <see cref="Update(Size, bool)"/> on every pointer event and <see cref="Update()"/> when a wait
/// it asked for has passed. All calls must come from one thread. See
/// docs/features/input/hot-corners.md.
/// </summary>
public sealed class CornerDetector(IHotCornerEnvironment environment, TimeProvider time)
{
    /// <summary>How far from each end of an edge, in effective pixels, its push action doesn't trigger.</summary>
    public const int CornerZoneEffectivePixels = 40;

    /// <summary>How far along an edge, in effective pixels, the pointer can move before a push starts again.</summary>
    public const int DriftEffectivePixels = 16;

    /// <summary>How long movement against an edge counts towards a push.</summary>
    public static readonly TimeSpan PushWindow = TimeSpan.FromSeconds(1);

    /// <summary>How long an edge waits for a device that reports positions, unless the activation delay is longer.</summary>
    public static readonly TimeSpan EdgePositionDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How long after it triggers a corner or edge doesn't trigger again, unless the activation delay and 50 ms is longer.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(350);

    private readonly Queue<(long Time, int Amount)> _pushes = new();
    private HotCornersSettings _settings = new();
    private Spot[] _spots = [];
    private Rectangle _allDisplays;
    private Spot? _current;
    private long _enteredAt;
    private bool _finished;
    private bool _byPosition;
    private int _pushSum;
    private int _pushPeak;
    private int _pushStart;
    private bool _pushEnough;
    private Spot? _triggered;
    private long _triggeredAt;

    /// <summary>
    /// The corner or edge the pointer is on, among those that have an action or a wheel action, as
    /// of the last call to <see cref="Configure(HotCornersSettings, IReadOnlyList{DisplayDetails})"/>
    /// or <see cref="Update(Size, bool)"/>; <see langword="null"/> when it's on none.
    /// </summary>
    public ScreenCorner? CurrentCorner => _current?.Position;

    /// <summary>Where the pointer was at the last call to <see cref="Update(Size, bool)"/>.</summary>
    public Point? LastPosition { get; private set; }

    /// <summary>The area of the display of the corner or edge the pointer is on.</summary>
    public Rectangle? CurrentDisplay => _current?.Display;

    /// <summary>
    /// The corner or edge whose wheel action applies as long as the pointer stays there: the checks
    /// passed as the pointer got there. <see langword="null"/> while the pointer is elsewhere, or
    /// they didn't pass.
    /// </summary>
    public WheelSpot? Wheel { get; private set; }

    /// <summary>
    /// Sets the settings and the displays, at 100% scale. A corner or edge the pointer is on already
    /// doesn't trigger until the pointer leaves it and comes back.
    /// </summary>
    public void Configure(HotCornersSettings settings, IReadOnlyList<Rectangle> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);

        Configure(settings, [.. displays.Select(area => new DisplayDetails(area, area, 96))]);
    }

    /// <summary>
    /// Sets the settings and the displays, in physical pixels. A corner or edge the pointer is on
    /// already doesn't trigger until the pointer leaves it and comes back, and its wheel action
    /// applies once the pointer comes back.
    /// </summary>
    public void Configure(HotCornersSettings settings, IReadOnlyList<DisplayDetails> displays)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(displays);

        _settings = settings;
        var areas = displays.Select(display => display.Bounds).ToList();
        _spots = [.. displays
            .SelectMany(display => CornersOf(display, areas).Concat(EdgesOf(display, areas)))
            .Where(spot => settings.For(spot.Position) is { } place && (place.Action != HotCornerAction.None || place.Wheel != HotCornerWheelAction.None))];
        _allDisplays = areas.Count == 0 ? Rectangle.Empty : areas.Aggregate(Rectangle.Union);
        _current = Find(environment.PointerPosition);
        _finished = true;
        _triggered = null;
        Wheel = null;
        ResetPush(environment.PointerPosition);
    }

    /// <summary>Decides again without a pointer event, as when a wait has passed.</summary>
    public CornerDecision Update() => Update(Size.Empty, isPosition: false);

    /// <summary>Decides for a pointer event: how far the mouse moved, or whether it reported a position.</summary>
    public CornerDecision Update(Size movement, bool isPosition)
    {
        var position = environment.PointerPosition;
        LastPosition = position;
        var spot = Find(position);
        var arrived = spot != _current;
        if (arrived)
        {
            // A corner or edge the pointer was seen leaving while waiting. One it passes straight
            // into, as between displays, waits for its own delay instead, and a pointer that can't
            // be read, as on the lock screen, didn't leave early.
            var leftEarly = !_finished && _current is { } left && spot is null && position is not null && HadBegun(left) ? left : null;
            _current = spot;
            _enteredAt = time.GetTimestamp();
            _finished = false;
            _byPosition = false;
            ResetPush(position);
            Wheel = WheelOf(spot);

            if (leftEarly is { } early)
            {
                return new CornerDecision(CornerOutcome.LeftEarly, early.Position, ActionOf(early), TimeSpan.Zero);
            }

            // Dragging a window to a corner or edge snaps it there; releasing the button before it
            // would trigger must not trigger it. Dragging to an edge is ordinary use, so it isn't
            // a result.
            if (spot is { } entered && ActionOf(entered) != HotCornerAction.None && environment.IsMouseButtonHeld)
            {
                return entered.IsEdge ? FinishQuietly() : Finish(entered, CornerOutcome.MouseButtonHeld);
            }
        }

        if (_current is not { } current || _finished)
        {
            return CornerDecision.Nothing;
        }

        // A corner or edge with only a wheel action never triggers.
        if (ActionOf(current) == HotCornerAction.None)
        {
            return FinishQuietly();
        }

        // The move that brought the pointer here isn't a push: a quick flick to a scroll bar can
        // move the mouse as far in one event.
        if (isPosition)
        {
            _byPosition = true;
        }
        else if (!arrived && movement != Size.Empty && position is { } at)
        {
            AddPush(current, movement, at);
        }

        var now = time.GetTimestamp();
        var wait = TimeSpan.Zero;
        if (UsesDelay(current))
        {
            var delay = DelayOf(current);
            var elapsed = time.GetElapsedTime(_enteredAt, now);
            if (elapsed < delay)
            {
                wait = delay - elapsed;
            }
        }
        else if (_pushEnough || _pushSum >= _settings.PushCounts)
        {
            _pushEnough = true;
        }
        else
        {
            return CornerDecision.Nothing;
        }

        // A hand that slips out and back right after a trigger mustn't undo the action at once.
        if (_triggered == current)
        {
            var cooldown = TimeSpan.FromMilliseconds(Math.Max(Cooldown.TotalMilliseconds, _settings.ActivationDelayMilliseconds + 50));
            var since = time.GetElapsedTime(_triggeredAt, now);
            if (since < cooldown && cooldown - since > wait)
            {
                wait = cooldown - since;
            }
        }

        if (wait > TimeSpan.Zero)
        {
            return new CornerDecision(CornerOutcome.Waiting, current.Position, ActionOf(current), wait);
        }

        // A corner that an app pins the pointer to isn't one the user aimed for, so a captured
        // pointer skips it whatever the full-screen setting.
        var key = _settings.For(current.Position).Key;
        if (environment.IsMouseButtonHeld && current.IsEdge)
        {
            return FinishQuietly();
        }

        return Finish(
            current,
            environment.IsMouseButtonHeld ? CornerOutcome.MouseButtonHeld
            : current.IsEdge && environment.HasAutoHideBar(current.Display, SideOf(current.Position)) ? CornerOutcome.TaskbarHides
            : key != HotCornerKey.None && !environment.IsKeyHeld(key) ? CornerOutcome.KeyNotHeld
            : IsPointerConfined() ? CornerOutcome.PointerConfined
            : environment.PointerVisibility == PointerVisibility.Hidden ? CornerOutcome.PointerHidden
            : _settings.IgnoreFullScreenApps && FullScreenCheck.IsFullScreen(environment, current.Display) ? CornerOutcome.FullScreenApp
            : IsListedAppInFront() ? CornerOutcome.AppExcluded
            : CornerOutcome.Triggered);
    }

    /// <summary>The side of the display that an edge is on; for a corner, the side it's at the start of.</summary>
    public static ScreenSide SideOf(ScreenCorner place) => place switch
    {
        ScreenCorner.Left or ScreenCorner.TopLeft => ScreenSide.Left,
        ScreenCorner.Right or ScreenCorner.TopRight => ScreenSide.Right,
        ScreenCorner.Top => ScreenSide.Top,
        _ => ScreenSide.Bottom,
    };

    /// <summary>Whether the place is an edge rather than a corner.</summary>
    public static bool IsEdge(ScreenCorner place) => place is ScreenCorner.Top or ScreenCorner.Bottom or ScreenCorner.Left or ScreenCorner.Right;

    private static int Effective(int pixels, uint dpi) => (int)Math.Round(pixels * dpi / 96.0);

    private HotCornerAction ActionOf(Spot spot) => _settings.For(spot.Position).Action;

    // Corners use the delay unless they trigger when pushed; a device that reports positions
    // can't push, so it falls back to the delays.
    private bool UsesDelay(Spot spot) => _byPosition || (!spot.IsEdge && _settings.CornersTrigger == CornersTrigger.Delay);

    private TimeSpan DelayOf(Spot spot)
    {
        var delay = TimeSpan.FromMilliseconds(_settings.ActivationDelayMilliseconds);
        return spot.IsEdge && delay < EdgePositionDelay ? EdgePositionDelay : delay;
    }

    // Resting on an edge is ordinary use, so leaving one is a result only when a push had got
    // halfway, or a device that reports positions had stayed half the delay.
    private bool HadBegun(Spot spot)
    {
        if (!spot.IsEdge && _settings.CornersTrigger == CornersTrigger.Delay && !_byPosition)
        {
            return true;
        }

        return _byPosition
            ? time.GetElapsedTime(_enteredAt) * 2 >= DelayOf(spot)
            : _pushPeak * 2 >= _settings.PushCounts;
    }

    private void ResetPush(Point? position)
    {
        _pushes.Clear();
        _pushSum = 0;
        _pushPeak = 0;
        _pushEnough = false;
        _pushStart = _current is { } spot && position is { } at ? spot.Along(at) : 0;
    }

    // Movement outward adds up within the push window. Moving along an edge starts the push again
    // from there, and near the ends of an edge it doesn't count, so a pointer sliding into a corner
    // triggers the corner.
    private void AddPush(Spot spot, Size movement, Point position)
    {
        var now = time.GetTimestamp();
        if (spot.IsEdge)
        {
            var along = spot.Along(position);
            if (Math.Abs(along - _pushStart) > spot.Drift)
            {
                _pushes.Clear();
                _pushSum = 0;
                _pushStart = along;
            }

            if (spot.IsNearEnd(along))
            {
                return;
            }
        }

        var outward = spot.IsEdge
            ? (spot.OutX * movement.Width) + (spot.OutY * movement.Height)
            : Math.Max(0, spot.OutX * movement.Width) + Math.Max(0, spot.OutY * movement.Height);
        if (outward <= 0)
        {
            return;
        }

        _pushes.Enqueue((now, outward));
        _pushSum += outward;
        while (_pushes.Count > 0 && time.GetElapsedTime(_pushes.Peek().Time, now) > PushWindow)
        {
            _pushSum -= _pushes.Dequeue().Amount;
        }

        _pushPeak = Math.Max(_pushPeak, _pushSum);
    }

    // The wheel's checks run once, as the pointer gets there; a captured pointer is the hook's.
    private WheelSpot? WheelOf(Spot? spot)
    {
        if (spot is null || _settings.For(spot.Position) is not { Wheel: not HotCornerWheelAction.None } place
            || environment.IsMouseButtonHeld
            || (_settings.IgnoreFullScreenApps && FullScreenCheck.IsFullScreen(environment, spot.Display))
            || IsListedAppInFront())
        {
            return null;
        }

        return new WheelSpot(spot.Position, spot.Area, place.Wheel, place.WheelKey);
    }

    private bool IsPointerConfined() => environment.PointerClip is { } clip && !clip.Contains(_allDisplays);

    // The app in front is read only while apps are listed.
    private bool IsListedAppInFront() =>
        _settings.ExcludedApps.Count > 0
        && environment.AppInFront is { } app
        && _settings.ExcludedApps.Any(listed => listed.Matches(app));

    private CornerDecision Finish(Spot spot, CornerOutcome outcome)
    {
        _finished = true;
        if (outcome == CornerOutcome.Triggered)
        {
            (_triggered, _triggeredAt) = (spot, time.GetTimestamp());
        }

        return new CornerDecision(outcome, spot.Position, ActionOf(spot), TimeSpan.Zero);
    }

    private CornerDecision FinishQuietly()
    {
        _finished = true;
        return CornerDecision.Nothing;
    }

    // Corners come first, so one wins over an edge through it.
    private Spot? Find(Point? position)
    {
        if (position is not { } point)
        {
            return null;
        }

        foreach (var spot in _spots)
        {
            if (spot.Area.Contains(point))
            {
                return spot;
            }
        }

        return null;
    }

    // A corner that touches another display, even diagonally, isn't one: the pointer passes into
    // the other display there instead of stopping.
    private static IEnumerable<Spot> CornersOf(DisplayDetails details, IReadOnlyList<Rectangle> displays)
    {
        var display = details.Bounds;
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
                yield return new Spot(position, display, new Rectangle(x, y, 1, 1), outwardX, outwardY, 0, 0, 0, 0);
            }
        }
    }

    // An edge is each part of a side where no other display continues it, so the pointer stops there.
    private static IEnumerable<Spot> EdgesOf(DisplayDetails details, IReadOnlyList<Rectangle> displays)
    {
        var display = details.Bounds;
        if (display.Width <= 0 || display.Height <= 0)
        {
            yield break;
        }

        var zone = Effective(CornerZoneEffectivePixels, details.Dpi);
        var drift = Effective(DriftEffectivePixels, details.Dpi);
        (ScreenCorner Position, int OutX, int OutY)[] sides =
        [
            (ScreenCorner.Top, 0, -1),
            (ScreenCorner.Bottom, 0, 1),
            (ScreenCorner.Left, -1, 0),
            (ScreenCorner.Right, 1, 0),
        ];

        foreach (var (position, outX, outY) in sides)
        {
            var vertical = outX != 0;
            var (start, end) = vertical ? (display.Top, display.Bottom) : (display.Left, display.Right);
            var line = position switch
            {
                ScreenCorner.Top => display.Top,
                ScreenCorner.Bottom => display.Bottom - 1,
                ScreenCorner.Left => display.Left,
                _ => display.Right - 1,
            };
            var beyond = line + outX + outY;

            // The parts of the side that another display continues.
            var covered = displays
                .Where(other => other != display && (vertical ? other.Left <= beyond && beyond < other.Right : other.Top <= beyond && beyond < other.Bottom))
                .Select(other => vertical ? (Start: Math.Max(start, other.Top), End: Math.Min(end, other.Bottom)) : (Start: Math.Max(start, other.Left), End: Math.Min(end, other.Right)))
                .Where(part => part.Start < part.End)
                .OrderBy(part => part.Start);

            var from = start;
            foreach (var part in covered.Append((Start: end, End: end)))
            {
                if (part.Start > from)
                {
                    var area = vertical ? new Rectangle(line, from, 1, part.Start - from) : new Rectangle(from, line, part.Start - from, 1);
                    yield return new Spot(position, display, area, outX, outY, start, end, zone, drift);
                }

                from = Math.Max(from, part.End);
            }
        }
    }

    // A corner's pixel, or part of an edge's outermost line of pixels, with the way out of the
    // display and, for an edge, where its side starts and ends along it.
    private sealed record Spot(ScreenCorner Position, Rectangle Display, Rectangle Area, int OutX, int OutY, int SideStart, int SideEnd, int Zone, int Drift)
    {
        public bool IsEdge => CornerDetector.IsEdge(Position);

        // How far along the edge, for an edge on the left or right side, from the top.
        public int Along(Point point) => OutX != 0 ? point.Y : point.X;

        public bool IsNearEnd(int along) => along < SideStart + Zone || along >= SideEnd - Zone;
    }
}

/// <summary>A corner or edge whose wheel action applies while the pointer is on it.</summary>
/// <param name="Area">Where the wheel is kept from the apps: the corner's pixel or the edge's outermost line.</param>
public readonly record struct WheelSpot(ScreenCorner Position, Rectangle Area, HotCornerWheelAction Action, HotCornerKey Key);
