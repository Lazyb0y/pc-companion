namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// The shortcut that sends the window in front to the tray. The names are stored in the settings
/// file, so never rename one.
/// </summary>
public enum MinimizeToTrayShortcut
{
    /// <summary>No shortcut.</summary>
    None,

    /// <summary>The Windows key, Shift and Z.</summary>
    WindowsShiftZ,
}
