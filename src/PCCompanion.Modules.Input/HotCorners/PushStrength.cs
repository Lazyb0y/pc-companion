namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// How far the mouse must move against an edge, or into a corner that triggers when pushed, within
/// a second. The names are stored in the settings file, so never rename one.
/// </summary>
public enum PushStrength
{
    Light,
    Medium,
    Firm,
}