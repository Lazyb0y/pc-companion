namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// The shortcut that starts choosing an app to end. The names are stored in the settings file, so
/// never rename one.
/// </summary>
public enum EndAppShortcut
{
    /// <summary>No shortcut; the PC Companion icon's menu still has End an app.</summary>
    None,

    /// <summary>Ctrl, Alt and Esc.</summary>
    ControlAltEscape,

    /// <summary>The Windows key, Ctrl and Esc.</summary>
    WindowsControlEscape,
}
