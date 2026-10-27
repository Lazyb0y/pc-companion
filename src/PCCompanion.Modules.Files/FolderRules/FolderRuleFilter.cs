using System;
using System.IO;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// The files the rules never touch: folders, links, files kept online only, which reading would
/// download, hidden and system files, such as desktop.ini, and files still being written, by
/// their names; and the hidden system folders they never use. See "What rules never touch" and
/// "Which folders" in docs/features/files/folder-rules.md.
/// </summary>
public static class FolderRuleFilter
{
    public static bool IsLeftAlone(FileDetails file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.IsFolder || file.IsLink || file.IsOnlineOnly
            || (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0
            || ArrivalWatch.IsSkippedName(file.Name);
    }

    /// <summary>
    /// Whether a folder is one that only Windows uses, such as the Recycle Bin's own folders or
    /// <c>System Volume Information</c>, which are both hidden and system. A drive's root is too,
    /// so call it only for the folders in one. <c>AppData</c> is only hidden, and allowed.
    /// </summary>
    public static bool IsSystemFolder(FileDetails folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return folder.IsFolder && (folder.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System);
    }
}
