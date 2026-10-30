namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// The shortcut that minimizes every window of every other app, and brings them back when pressed
/// again. The names are stored in the settings file, so never rename one.
/// </summary>
public enum HideOthersShortcut
{
    /// <summary>No shortcut.</summary>
    None,

    /// <summary>The Windows key, Ctrl, Shift and H.</summary>
    WindowsControlShiftH,
}
