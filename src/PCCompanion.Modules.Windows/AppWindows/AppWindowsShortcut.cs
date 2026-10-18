namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// The shortcut that switches windows, with Shift to go back. The names are stored in the settings
/// file, so never rename one.
/// </summary>
public enum AppWindowsShortcut
{
    /// <summary>Alt and the key above Tab.</summary>
    AltAboveTab,

    /// <summary>The Windows key and the key above Tab.</summary>
    WindowsAboveTab,
}
