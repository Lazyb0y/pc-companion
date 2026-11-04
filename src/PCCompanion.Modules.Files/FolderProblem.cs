namespace PCCompanion.Modules.Files;

/// <summary>What keeps a folder from being watched, or from being a rule's destination.</summary>
public enum FolderProblem
{
    None,
    NoPath,

    /// <summary>
    /// The path holds characters Windows doesn't allow or wildcards, names a stream of a file, or
    /// reaches a device directly, such as <c>\\?\C:\</c>.
    /// </summary>
    InvalidPath,

    /// <summary>
    /// The path doesn't start with a drive or a server and a folder it shares, such as <c>D:\</c>
    /// or <c>\\server\share</c>.
    /// </summary>
    NotFullPath,

    /// <summary>The Windows folder or a folder in it, which only administrators can change.</summary>
    WindowsFolder,

    /// <summary>A Program Files folder or a folder in one, which only administrators can change.</summary>
    ProgramFiles,

    /// <summary>PC Companion's data folder or a folder in it, where a rule could move its settings.</summary>
    DataFolder,

    /// <summary>
    /// A hidden system folder or a folder in one, such as the Recycle Bin's own folders, which only
    /// Windows uses. Only the host and the page, which read the disk, find it.
    /// </summary>
    SystemFolder,

    /// <summary>
    /// A destination's subfolder made from the file has a part in braces that isn't known or
    /// closed, a character a folder name can't have, or is <c>.</c> or <c>..</c>.
    /// </summary>
    InvalidPart,

    /// <summary>A folder earlier in the list is the same folder.</summary>
    AlreadyWatched,

    /// <summary>It comes after the first <see cref="FolderRules.FolderRulesSettings.MaximumFolders"/> folders.</summary>
    TooMany,
}
