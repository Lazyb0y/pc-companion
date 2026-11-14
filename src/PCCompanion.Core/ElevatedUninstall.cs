namespace PCCompanion.Core;

/// <summary>
/// What the elevated helper removes for every user when setup uninstalls an install for all users,
/// besides the startup tasks of <see cref="ElevatedStartup"/>. Setup runs it as SYSTEM.
/// </summary>
public static class ElevatedUninstall
{
    /// <summary>
    /// Helper command: remove every user's Remote Folders folders: unregistered with Windows, and
    /// gone from each user's navigation pane.
    /// </summary>
    public const string RemoveAllRemoteFoldersCommand = "remove-all-remote-folders";

    /// <summary>The start of Remote Folders' sync root IDs, <c>PCCompanion!&lt;user SID&gt;!&lt;server ID&gt;</c>.</summary>
    public const string RemoteFoldersSyncRootPrefix = "PCCompanion!";
}
