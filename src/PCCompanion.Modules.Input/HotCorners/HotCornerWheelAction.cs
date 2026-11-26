namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What the wheel does with the pointer on a corner or edge: each notch up does the first, each
/// notch down the second. The names are stored in the settings file, so never rename one.
/// </summary>
public enum HotCornerWheelAction
{
    None,

    /// <summary>Louder and quieter, with the volume keys.</summary>
    Volume,

    /// <summary>The previous and next virtual desktop.</summary>
    Desktops,

    /// <summary>The previous and next track of the app playing media.</summary>
    Tracks,
}