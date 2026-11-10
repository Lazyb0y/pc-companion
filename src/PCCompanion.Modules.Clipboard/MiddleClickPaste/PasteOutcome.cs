namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>
/// How a middle-click on a text box ended; logged by name. Every outcome but
/// <see cref="Pasted"/> shows its notice; see "When nothing is pasted" in
/// docs/features/clipboard/middle-click-paste.md.
/// </summary>
public enum PasteOutcome
{
    /// <summary>The text went in.</summary>
    Pasted,

    /// <summary>Nothing is selected in the window in front, or only all of the clicked box's own text.</summary>
    NothingSelected,

    /// <summary>The app with the selection doesn't share it, or answered with an error.</summary>
    NotShared,

    /// <summary>The selection is in a password box, which is never read.</summary>
    Password,

    /// <summary>The app with the selection runs as administrator, while PC Companion doesn't.</summary>
    AppRunsAsAdministrator,

    /// <summary>PC Companion runs as administrator, and the app with the selection doesn't.</summary>
    RunsAsAdministrator,

    /// <summary>The app with the selection runs with fewer rights, such as some Store apps.</summary>
    AppHasFewerRights,

    /// <summary>The app with the selection isn't responding, or didn't answer in time.</summary>
    AppNotResponding,

    /// <summary>The selected text is longer than the limit.</summary>
    TooLong,

    /// <summary>The box doesn't take pasted text.</summary>
    BoxRefused,

    /// <summary>The box's app isn't responding.</summary>
    BoxNotResponding,

    /// <summary>The pointer moved away from the click before the text could be dropped there.</summary>
    PointerMoved,

    /// <summary>Another paste is still in progress.</summary>
    Busy,
}
