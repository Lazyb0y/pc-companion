using System;
using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What one corner or edge does: an action when the pointer reaches it, and one when the wheel turns on it.
/// </summary>
public sealed record CornerSettings
{
    public HotCornerAction Action { get; set; } = HotCornerAction.None;

    /// <summary>
    /// The key that must be held when the pointer reaches the corner or edge, or <see cref="HotCornerKey.None"/>.
    /// </summary>
    public HotCornerKey Key { get; set; } = HotCornerKey.None;

    /// <summary>
    /// For <see cref="HotCornerAction.RunProgram"/>, the full path of the program. Kept when the
    /// action changes, so choosing Run a program again brings it back.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Program { get; set; }

    /// <summary>
    /// For <see cref="HotCornerAction.RunProgram"/>, the program's arguments, as typed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    /// <summary>
    /// What the wheel does with the pointer on the corner or edge. Left out of the file while it's
    /// <see cref="HotCornerWheelAction.None"/>, so settings from before the wheel stay as they were.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public HotCornerWheelAction Wheel { get; set; } = HotCornerWheelAction.None;

    /// <summary>
    /// The key that must be held as the wheel turns, or <see cref="HotCornerKey.None"/>; separate
    /// from <see cref="Key"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public HotCornerKey WheelKey { get; set; } = HotCornerKey.None;

    // A number in the file reads as any enum value, named or not.
    internal CornerSettings Checked() =>
        Enum.IsDefined(Action) && Enum.IsDefined(Key) && Enum.IsDefined(Wheel) && Enum.IsDefined(WheelKey) ? this : new CornerSettings();
}
