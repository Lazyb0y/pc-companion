using System;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What <see cref="CornerDetector.Update"/> found.
/// </summary>
/// <param name="Wait">With <see cref="CornerOutcome.Waiting"/>, how long until the corner can trigger.</param>
public readonly record struct CornerDecision(CornerOutcome Outcome, ScreenCorner Corner, HotCornerAction Action, TimeSpan Wait)
{
    public static CornerDecision Nothing { get; }
}

public enum CornerOutcome
{
    /// <summary>
    /// The pointer isn't in a corner that has an action, or the corner has already triggered or been skipped.
    /// </summary>
    None,

    /// <summary>
    /// The pointer is in a corner; call <see cref="CornerDetector.Update"/> again after the wait.
    /// </summary>
    Waiting,

    /// <summary>
    /// Run the action now.
    /// </summary>
    Triggered,

    MouseButtonHeld,

    KeyNotHeld,

    /// <summary>
    /// An app confined the pointer to part of the screen, as games and virtual machines do.
    /// </summary>
    PointerConfined,

    /// <summary>
    /// An app hid the pointer, as browser games do.
    /// </summary>
    PointerHidden,

    /// <summary>
    /// An app is full screen on the corner's display.
    /// </summary>
    FullScreenApp,
}
