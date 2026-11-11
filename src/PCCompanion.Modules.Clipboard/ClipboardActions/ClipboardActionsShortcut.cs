namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>The shortcut that shows the actions for the copied text.</summary>
public enum ClipboardActionsShortcut
{
    /// <summary>No shortcut; Shortcuts & Mouse Buttons can give the menu others.</summary>
    None,

    /// <summary>The Windows key, Alt and V.</summary>
    WindowsAltV,
}

/// <summary>The shortcut that shows the actions for the selected text, while Selected text is on.</summary>
public enum ClipboardActionsSelectionShortcut
{
    /// <summary>No shortcut; Shortcuts & Mouse Buttons can give the menu others.</summary>
    None,

    /// <summary>The Windows key, Alt and S.</summary>
    WindowsAltS,
}
