namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// What Windows + Alt + S found, as its menu says; logged by name. See "When the selected text
/// can't be read" in docs/features/clipboard/clipboard-actions.md.
/// </summary>
public enum SelectionStatus
{
    /// <summary>The selected text was read.</summary>
    Read,

    /// <summary>The app in front has no text selected.</summary>
    NothingSelected,

    /// <summary>The app doesn't give its selected text, or answered with an error.</summary>
    NotShared,

    /// <summary>The keyboard focus is in a password box, which isn't read.</summary>
    Password,

    /// <summary>The app runs as administrator while PC Companion doesn't.</summary>
    AppRunsAsAdministrator,

    /// <summary>PC Companion runs as administrator and the app doesn't.</summary>
    RunsAsAdministrator,

    /// <summary>The app runs with fewer rights, such as in an AppContainer.</summary>
    AppHasFewerRights,

    /// <summary>Windows shows a window in place of the app, which stopped responding.</summary>
    NotResponding,

    /// <summary>The app didn't give its selected text in time.</summary>
    NoAnswer,

    /// <summary>The selected text is longer than 4,096 characters.</summary>
    TooLong,
}
