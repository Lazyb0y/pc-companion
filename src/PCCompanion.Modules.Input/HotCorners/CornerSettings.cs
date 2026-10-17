using System;
using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What one corner does.
/// </summary>
public sealed record CornerSettings
{
    public HotCornerAction Action { get; set; } = HotCornerAction.None;

    /// <summary>
    /// The key that must be held when the pointer reaches the corner, or <see cref="HotCornerKey.None"/>.
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

    // A number in the file reads as any enum value, named or not.
    internal CornerSettings Checked() => Enum.IsDefined(Action) && Enum.IsDefined(Key) ? this : new CornerSettings();
}
