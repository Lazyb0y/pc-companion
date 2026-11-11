using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What <see cref="PointerHook"/> keeps from the apps: a middle-click, a Back or Forward click or a
/// wheel notch that a claim asks for, and the release of a click it kept. It remembers what it did
/// with recent input, by the input's time, for <see cref="Outcome"/>. Its decisions take
/// microseconds and call no other program. Call <see cref="Decide"/> on one thread;
/// <see cref="Outcome"/> on any.
/// </summary>
public sealed class PointerKeeper
{
    /// <summary>How many clicks and wheel notches are remembered for <see cref="Outcome"/>.</summary>
    public const int HistoryLength = 64;

    private readonly Lock _gate = new();
    private readonly (uint Time, bool Kept)[] _history = new (uint, bool)[HistoryLength];

    // Whether the press of each button, by PointerButton, was kept, so its release is kept too.
    private readonly bool[] _pressKept = new bool[4];
    private int _count;
    private int _next;

    /// <summary>
    /// Decides whether to keep one event from the apps.
    /// </summary>
    /// <param name="claims">The claims to keep input for; the first that keeps it wins.</param>
    /// <param name="input">What happened: a button going down or up, or a wheel notch.</param>
    /// <param name="point">Where the pointer was.</param>
    /// <param name="keys">The keys held at the time.</param>
    /// <param name="time">When the input happened, as Windows stamps it.</param>
    /// <param name="place">
    /// Asked only for a claim that needs it: the top-level window under the pointer, and whether an
    /// app captured the pointer. Without it, neither keeps a claim from applying.
    /// </param>
    public PointerDecision Decide(IReadOnlyList<PointerClaim> claims, PointerInput input, Point point, PointerKeys keys, uint time, IPointerPlace? place = null)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var (button, isRelease) = Read(input);
        if (isRelease)
        {
            var keptPress = _pressKept[(int)button];
            _pressKept[(int)button] = false;
            return new PointerDecision(keptPress, -1);
        }

        var trigger = new PointerTrigger(button, keys);
        var claim = -1;
        bool? captured = null;
        nint? window = null;
        for (var index = 0; index < claims.Count; index++)
        {
            var candidate = claims[index];
            if (!candidate.Keeps(trigger, point)
                || (candidate.NotWhileCaptured && (captured ??= place?.IsPointerCaptured() ?? false))
                || (candidate.ExceptIn.TryGetValue(trigger, out var windows) && windows.Count > 0 && windows.Contains(window ??= place?.WindowAt(point) ?? 0)))
            {
                continue;
            }

            claim = index;
            break;
        }

        if (button != PointerButton.Wheel)
        {
            _pressKept[(int)button] = claim >= 0;
        }

        Remember(time, claim >= 0);
        return new PointerDecision(claim >= 0, claim);
    }

    /// <summary>
    /// What was done with the middle-click or wheel notch stamped <paramref name="time"/>: kept, let
    /// through, or never seen, as when Windows removed the hook.
    /// </summary>
    public PointerInputOutcome Outcome(uint time)
    {
        lock (_gate)
        {
            for (var step = 1; step <= _count; step++)
            {
                var entry = _history[(_next - step + HistoryLength) % HistoryLength];
                if (entry.Time == time)
                {
                    return entry.Kept ? PointerInputOutcome.Kept : PointerInputOutcome.LetThrough;
                }
            }

            return PointerInputOutcome.NotSeen;
        }
    }

    private static (PointerButton Button, bool IsRelease) Read(PointerInput input) => input switch
    {
        PointerInput.MiddleDown => (PointerButton.Middle, false),
        PointerInput.MiddleUp => (PointerButton.Middle, true),
        PointerInput.BackDown => (PointerButton.Back, false),
        PointerInput.BackUp => (PointerButton.Back, true),
        PointerInput.ForwardDown => (PointerButton.Forward, false),
        PointerInput.ForwardUp => (PointerButton.Forward, true),
        _ => (PointerButton.Wheel, false),
    };

    private void Remember(uint time, bool kept)
    {
        lock (_gate)
        {
            _history[_next] = (time, kept);
            _next = (_next + 1) % HistoryLength;
            _count = Math.Min(_count + 1, HistoryLength);
        }
    }
}

/// <summary>A click or wheel move to keep from the apps in an area of the screen.</summary>
/// <param name="Area">Where, in physical pixels: the pointer must be inside.</param>
/// <param name="Triggers">Which clicks and wheel moves, each with the keys that must be held.</param>
public sealed record PointerClaim(Rectangle Area, IReadOnlySet<PointerTrigger> Triggers)
{
    private static readonly IReadOnlyDictionary<PointerTrigger, IReadOnlySet<nint>> NoWindows = new Dictionary<PointerTrigger, IReadOnlySet<nint>>();

    /// <summary>
    /// For each trigger that has some, the top-level windows over which it isn't kept, so the app
    /// under the pointer gets the click, such as the windows of the apps a trigger skips.
    /// </summary>
    public IReadOnlyDictionary<PointerTrigger, IReadOnlySet<nint>> ExceptIn { get; init; } = NoWindows;

    /// <summary>Whether the claim keeps nothing while an app has hidden the pointer or confined it, as games do.</summary>
    public bool NotWhileCaptured { get; init; }

    /// <summary>Whether this claim's area and triggers keep <paramref name="trigger"/> with the pointer at <paramref name="point"/>.</summary>
    public bool Keeps(PointerTrigger trigger, Point point) => Area.Contains(point) && Triggers.Contains(trigger);
}

/// <summary>What <see cref="PointerKeeper"/> asks about the pointer's place, only for a claim that needs it.</summary>
public interface IPointerPlace
{
    /// <summary>The top-level window under <paramref name="point"/>, or 0.</summary>
    nint WindowAt(Point point);

    /// <summary>Whether an app has hidden the pointer or confined it to less than all the displays.</summary>
    bool IsPointerCaptured();
}

/// <summary>A click or wheel move with the keys held for it; it matches only those keys exactly.</summary>
public readonly record struct PointerTrigger(PointerButton Button, PointerKeys Keys);

/// <summary>What a <see cref="PointerTrigger"/> is.</summary>
public enum PointerButton
{
    /// <summary>A middle-click.</summary>
    Middle,

    /// <summary>A notch of the vertical wheel, either way.</summary>
    Wheel,

    /// <summary>The Back button, XBUTTON1.</summary>
    Back,

    /// <summary>The Forward button, XBUTTON2.</summary>
    Forward,
}

/// <summary>The keys held with a click or a wheel move.</summary>
[Flags]
public enum PointerKeys
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Windows = 8,
}

/// <summary>The input <see cref="PointerKeeper"/> decides on.</summary>
public enum PointerInput
{
    MiddleDown,
    MiddleUp,
    Wheel,
    BackDown,
    BackUp,
    ForwardDown,
    ForwardUp,
}

/// <summary>What <see cref="PointerKeeper.Decide"/> chose.</summary>
/// <param name="Keep">Whether the input is kept from the apps.</param>
/// <param name="Claim">The index of the claim it's kept for, which hears of it; -1 when no claim does, as for the release of a kept click.</param>
public readonly record struct PointerDecision(bool Keep, int Claim);

/// <summary>What the shared pointer hook did with a click or a wheel notch.</summary>
public enum PointerInputOutcome
{
    /// <summary>The hook never saw it: it wasn't set, or Windows removed it.</summary>
    NotSeen,

    /// <summary>The hook saw it and let it through to the app.</summary>
    LetThrough,

    /// <summary>The hook kept it from the app.</summary>
    Kept,
}
