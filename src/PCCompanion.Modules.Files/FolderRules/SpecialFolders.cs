using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// The folders that rules can't watch or move files to, and the folders where programs keep
/// their own files. See "Which folders" in docs/features/files/folder-rules.md.
/// </summary>
/// <param name="Windows">The Windows folder.</param>
/// <param name="ProgramFiles">The Program Files folders.</param>
/// <param name="DataFolder">PC Companion's data folder, which holds its settings.</param>
/// <param name="ProgramData">The <c>AppData</c> and <c>ProgramData</c> folders, which get a warning.</param>
public sealed record SpecialFolders(string Windows, IReadOnlyList<string> ProgramFiles, string DataFolder, IReadOnlyList<string> ProgramData)
{
    /// <summary>The current user's folders, with <paramref name="dataFolder"/> as PC Companion's data folder.</summary>
    public static SpecialFolders ForCurrentUser(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);

        var localAppData = Folder(Environment.SpecialFolder.LocalApplicationData);
        return new SpecialFolders(
            Folder(Environment.SpecialFolder.Windows),
            Present(Folder(Environment.SpecialFolder.ProgramFiles), Folder(Environment.SpecialFolder.ProgramFilesX86), Environment.GetEnvironmentVariable("ProgramFiles(Arm)")),
            dataFolder,
            Present(
                Path.GetDirectoryName(localAppData),
                localAppData,
                Folder(Environment.SpecialFolder.ApplicationData),
                Folder(Environment.SpecialFolder.CommonApplicationData)));
    }

    private static string Folder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    // A folder Windows doesn't have, such as Program Files (Arm) on an x64 PC, is left out.
    private static string[] Present(params string?[] folders) =>
        [.. folders.Where(folder => !string.IsNullOrEmpty(folder)).Select(folder => folder!)];
}
