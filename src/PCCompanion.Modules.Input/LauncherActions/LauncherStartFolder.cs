using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>
/// The folder of Launcher Actions' entries in the user's Start, such as "PC Companion actions" in
/// <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs</c>. A hidden file marks it, so it's found
/// again after its name changed with PC Companion's language. Nothing else in Start is touched, and
/// links in the folder are deleted rather than followed. See "Start entries" in
/// docs/features/input/launcher-actions.md.
/// </summary>
/// <param name="programs">The user's Start folder of programs.</param>
/// <param name="shortcuts">Reads and writes shortcut files; tests supply their own.</param>
public sealed class LauncherStartFolder(string programs, IShortcutFiles shortcuts)
{
    /// <summary>The hidden file that marks the folder as Launcher Actions'.</summary>
    public const string MarkerName = "PCCompanion.LauncherActions";

    private const string ShortcutExtension = ".lnk";

    /// <summary>The folder in the user's own Start, with shortcut files as Windows writes them.</summary>
    public LauncherStartFolder()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.Programs), new WindowsShortcutFiles())
    {
    }

    /// <summary>
    /// Makes the folder named <paramref name="folderName"/> hold <paramref name="entries"/>, each
    /// starting <paramref name="program"/>, and nothing else: it renames the folder found by its
    /// mark, writes the entries that are missing or differ, and deletes the rest. With no entries,
    /// it deletes the folder. Returns the number of entries written.
    /// </summary>
    /// <exception cref="IOException">A file couldn't be written or deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Windows refused a file.</exception>
    public int Update(string folderName, IReadOnlyList<StartEntry> entries, string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderName);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(program);

        if (entries.Count == 0)
        {
            Delete();
            return 0;
        }

        var folder = Path.Combine(programs, folderName);
        PrepareFolder(folder);

        var wanted = entries.ToDictionary(entry => entry.Name + ShortcutExtension, StringComparer.OrdinalIgnoreCase);
        foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if (item.Name.Equals(MarkerName, StringComparison.OrdinalIgnoreCase) && item is FileInfo && !IsLink(item))
            {
                continue;
            }

            if (IsLink(item) || item is DirectoryInfo || !wanted.ContainsKey(item.Name))
            {
                DeleteItem(item);
            }
        }

        var written = 0;
        foreach (var (fileName, entry) in wanted)
        {
            var path = Path.Combine(folder, fileName);
            var content = new ShortcutContent(program, entry.Arguments, entry.Description);
            if (File.Exists(path) && shortcuts.Read(path) == content)
            {
                continue;
            }

            shortcuts.Write(path, content);
            written++;
        }

        return written;
    }

    /// <summary>
    /// Deletes every folder of Launcher Actions' entries, found by its mark, as when the feature or
    /// Show actions in Start is turned off, or PC Companion is uninstalled.
    /// </summary>
    /// <exception cref="IOException">A file couldn't be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Windows refused a file.</exception>
    public void Delete()
    {
        foreach (var folder in MarkedFolders())
        {
            DeleteItem(folder);
        }
    }

    // A link to a folder or a file elsewhere is deleted itself, never what it leads to.
    private static bool IsLink(FileSystemInfo item) => item.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static void DeleteItem(FileSystemInfo item)
    {
        if (item is DirectoryInfo directory)
        {
            // Without following links: .NET deletes a link inside a folder, not what it leads to.
            directory.Delete(recursive: !IsLink(directory));
        }
        else
        {
            item.Attributes = FileAttributes.Normal;
            item.Delete();
        }
    }

    // The folders of Start's programs that the mark says are Launcher Actions', never links.
    private List<DirectoryInfo> MarkedFolders()
    {
        var start = new DirectoryInfo(programs);
        if (!start.Exists)
        {
            return [];
        }

        return [.. start.EnumerateDirectories().Where(folder => !IsLink(folder) && File.Exists(Path.Combine(folder.FullName, MarkerName)))];
    }

    // The folder under its current name, renamed from an older one or created, with its mark.
    private void PrepareFolder(string folder)
    {
        var target = new DirectoryInfo(folder);
        if (target.Exists && IsLink(target))
        {
            DeleteItem(target);
        }

        foreach (var marked in MarkedFolders().Where(marked => !string.Equals(marked.FullName, target.FullName, StringComparison.OrdinalIgnoreCase)))
        {
            if (Directory.Exists(folder))
            {
                DeleteItem(marked);
            }
            else
            {
                marked.MoveTo(folder);
            }
        }

        Directory.CreateDirectory(folder);
        var marker = new FileInfo(Path.Combine(folder, MarkerName));
        if (marker.Exists && IsLink(marker))
        {
            DeleteItem(marker);
        }

        if (!File.Exists(marker.FullName))
        {
            File.WriteAllBytes(marker.FullName, []);
            File.SetAttributes(marker.FullName, FileAttributes.Hidden | FileAttributes.System);
        }
    }
}

/// <summary>Reads and writes shortcut files, so tests can supply their own.</summary>
public interface IShortcutFiles
{
    /// <summary>What the shortcut at <paramref name="path"/> starts, or <see langword="null"/>.</summary>
    ShortcutContent? Read(string path);

    /// <summary>Writes a shortcut at <paramref name="path"/>, replacing one that's there.</summary>
    void Write(string path, ShortcutContent content);
}

/// <summary>Shortcut files as Windows writes them, through <see cref="ShellShortcut"/>.</summary>
public sealed class WindowsShortcutFiles : IShortcutFiles
{
    public ShortcutContent? Read(string path) => ShellShortcut.Read(path);

    public void Write(string path, ShortcutContent content) => ShellShortcut.Write(path, content);
}
