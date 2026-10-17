using System;

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

    // A number in the file reads as any enum value, named or not.
    internal CornerSettings Checked() => Enum.IsDefined(Action) && Enum.IsDefined(Key) ? this : new CornerSettings();
}
