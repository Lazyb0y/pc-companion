using System;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Turns the wheel's moves on a corner or edge into steps of its action: a step for each notch,
/// with smaller moves, as from mice that scroll smoothly, adding up to notches. Desktops and tracks
/// take at most one step every <see cref="SwitchInterval"/>, so a wheel that spins freely doesn't
/// race through them. Call it from one thread.
/// </summary>
public sealed class WheelSteps(TimeProvider time)
{
    /// <summary>A notch of the wheel.</summary>
    public const int Notch = 120;

    /// <summary>The shortest time between two steps of Desktops or Tracks.</summary>
    public static readonly TimeSpan SwitchInterval = TimeSpan.FromMilliseconds(300);

    private int _remainder;
    private long? _lastSwitch;

    /// <summary>Forgets a part of a notch, as when the pointer leaves the corner or edge.</summary>
    public void Reset() => _remainder = 0;

    /// <summary>
    /// Adds a move of the wheel, positive away from the user, and returns how many steps to take:
    /// positive up, negative down, 0 for none.
    /// </summary>
    public int Add(HotCornerWheelAction action, short delta)
    {
        // Turning the other way starts a new notch.
        if (_remainder != 0 && Math.Sign(delta) != Math.Sign(_remainder))
        {
            _remainder = 0;
        }

        _remainder += delta;
        var steps = _remainder / Notch;
        _remainder -= steps * Notch;
        if (steps == 0 || action is not (HotCornerWheelAction.Desktops or HotCornerWheelAction.Tracks))
        {
            return steps;
        }

        var now = time.GetTimestamp();
        if (_lastSwitch is { } last && time.GetElapsedTime(last, now) < SwitchInterval)
        {
            return 0;
        }

        _lastSwitch = now;
        return Math.Sign(steps);
    }
}
