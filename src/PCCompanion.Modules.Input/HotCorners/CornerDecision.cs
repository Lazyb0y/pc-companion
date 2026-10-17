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

    FullScreenApp,
}
