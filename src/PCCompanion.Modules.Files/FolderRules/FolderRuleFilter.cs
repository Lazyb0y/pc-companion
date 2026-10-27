using System;
using System.IO;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// The files the rules never touch: folders, links, files kept online only, which reading would
/// download, hidden and system files, such as desktop.ini, and files still being written, by
/// their names. See "What rules never touch" in docs/features/files/folder-rules.md.
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
}
