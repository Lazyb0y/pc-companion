namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// When corners trigger. Edges trigger only when pushed. The names are stored in the settings
/// file, so never rename one.
/// </summary>
public enum CornersTrigger
{
    /// <summary>When the pointer has stayed in the corner for the activation delay.</summary>
    Delay,

    /// <summary>When the pointer has pushed into the corner as hard as the push strength says.</summary>
    Push,
}