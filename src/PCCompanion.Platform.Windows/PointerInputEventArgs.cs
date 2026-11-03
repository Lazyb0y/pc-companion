using System;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A mouse event that <see cref="RawPointerMonitor"/> reports: a move, a button going down or up,
/// or the wheel turning.
/// </summary>
/// <param name="changes">Which buttons went down or up, and whether a wheel turned.</param>
/// <param name="wheelDelta">How far the vertical wheel turned: positive away from the user, 120 a notch.</param>
/// <param name="time">When the input happened, in milliseconds since Windows started.</param>
public sealed class PointerInputEventArgs(PointerButtonChanges changes, short wheelDelta, uint time) : EventArgs
{
    /// <summary>Which buttons went down or up, and whether a wheel turned; none for a move.</summary>
    public PointerButtonChanges Changes { get; } = changes;

    /// <summary>
    /// How far the vertical wheel turned: positive away from the user, 120 for a notch, less for a
    /// step of a mouse that scrolls smoothly; 0 when it didn't turn.
    /// </summary>
    public short WheelDelta { get; } = wheelDelta;

    /// <summary>
    /// When the input happened, in milliseconds since Windows started, as Windows stamps it for
    /// every program, so a low-level mouse hook gets the same time for the same input. It wraps
    /// around after about 49.7 days.
    /// </summary>
    public uint Time { get; } = time;

    /// <summary>
    /// The event raw input reports for the mouse: its button flags (<c>usButtonFlags</c>) and their
    /// data (<c>usButtonData</c>), which holds the wheel's delta when the vertical wheel turned.
    /// </summary>
    public static PointerInputEventArgs FromRawMouse(ushort buttonFlags, ushort buttonData, uint time)
    {
        var changes = (PointerButtonChanges)buttonFlags & PointerButtonChanges.All;
        var wheelDelta = (changes & PointerButtonChanges.Wheel) != 0 ? unchecked((short)buttonData) : (short)0;
        return new PointerInputEventArgs(changes, wheelDelta, time);
    }
}

/// <summary>What changed in a mouse event, as raw input's button flags say it.</summary>
[Flags]
public enum PointerButtonChanges : ushort
{
    None = 0,
    LeftDown = 0x0001,
    LeftUp = 0x0002,
    RightDown = 0x0004,
    RightUp = 0x0008,
    MiddleDown = 0x0010,
    MiddleUp = 0x0020,
    X1Down = 0x0040,
    X1Up = 0x0080,
    X2Down = 0x0100,
    X2Up = 0x0200,

    /// <summary>The vertical wheel turned; <see cref="PointerInputEventArgs.WheelDelta"/> says how far.</summary>
    Wheel = 0x0400,

    /// <summary>The horizontal wheel turned, or the wheel was tilted.</summary>
    HorizontalWheel = 0x0800,

    All = 0x0FFF,
}
