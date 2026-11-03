using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What <see cref="PointerHook"/> keeps from the apps: a middle-click or a wheel notch that a claim
/// asks for, and the release of a middle-click it kept. It remembers what it did with recent input,
/// by the input's time, for <see cref="Outcome"/>. Its decisions take microseconds and call no
/// other program. Call <see cref="Decide"/> on one thread; <see cref="Outcome"/> on any.
/// </summary>
public sealed class PointerKeeper
{
    /// <summary>How many middle-clicks and wheel notches are remembered for <see cref="Outcome"/>.</summary>
    public const int HistoryLength = 64;

    private readonly Lock _gate = new();
    private readonly (uint Time, bool Kept)[] _history = new (uint, bool)[HistoryLength];
    private int _count;
    private int _next;
    private bool _middleKept;

    /// <summary>
    /// Decides whether to keep one event from the apps.
    /// </summary>
    /// <param name="claims">The claims to keep input for; the first that keeps it wins.</param>
    /// <param name="input">What happened: a middle button going down or up, or a wheel notch.</param>
    /// <param name="point">Where the pointer was.</param>
    /// <param name="keys">The keys held at the time.</param>
    /// <param name="time">When the input happened, as Windows stamps it.</param>
    public PointerDecision Decide(IReadOnlyList<PointerClaim> claims, PointerInput input, Point point, PointerKeys keys, uint time)
    {
        ArgumentNullException.ThrowIfNull(claims);

        if (input == PointerInput.MiddleUp)
        {
            var keptPress = _middleKept;
            _middleKept = false;
            return new PointerDecision(keptPress, -1);
        }

        var trigger = new PointerTrigger(input == PointerInput.MiddleDown ? PointerButton.Middle : PointerButton.Wheel, keys);
        var claim = -1;
        for (var index = 0; index < claims.Count; index++)
        {
            if (claims[index].Keeps(trigger, point))
            {
                claim = index;
                break;
            }
        }

        if (input == PointerInput.MiddleDown)
        {
            _middleKept = claim >= 0;
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
    /// <summary>Whether this claim keeps <paramref name="trigger"/> with the pointer at <paramref name="point"/>.</summary>
    public bool Keeps(PointerTrigger trigger, Point point) => Area.Contains(point) && Triggers.Contains(trigger);
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
}

/// <summary>What <see cref="PointerKeeper.Decide"/> chose.</summary>
/// <param name="Keep">Whether the input is kept from the apps.</param>
/// <param name="Claim">The index of the claim it's kept for, which hears of it; -1 when no claim does, as for the release of a kept middle-click.</param>
public readonly record struct PointerDecision(bool Keep, int Claim);

/// <summary>What the shared pointer hook did with a middle-click or a wheel notch.</summary>
public enum PointerInputOutcome
{
    /// <summary>The hook never saw it: it wasn't set, or Windows removed it.</summary>
    NotSeen,

    /// <summary>The hook saw it and let it through to the app.</summary>
    LetThrough,

    /// <summary>The hook kept it from the app.</summary>
    Kept,
}
