namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// The shortcut that minimizes every window of the app in front. The names are stored in the
/// settings file, so never rename one.
/// </summary>
public enum HideAppShortcut
{
    /// <summary>No shortcut.</summary>
    None,

    /// <summary>The Windows key, Ctrl and H.</summary>
    WindowsControlH,
}
