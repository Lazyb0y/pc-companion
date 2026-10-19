using System;
using System.IO;

namespace PCCompanion.Infrastructure;

/// <summary>
/// Removes the user's data folder when PC Companion is uninstalled, with the rights of the process
/// that runs it. It never follows links: a link in the folder, or the folder itself if it's one, is
/// removed, and what it leads to stays.
/// </summary>
public static class UserDataRemoval
{
    /// <summary>
    /// Removes the data folder, or, with <paramref name="keepSettings"/>, everything in it but the
    /// settings file. Returns false when something couldn't be removed; the rest is removed anyway.
    /// </summary>
    public static bool Remove(AppDataPaths paths, bool keepSettings)
    {
        ArgumentNullException.ThrowIfNull(paths);

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(paths.Root);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint) || !attributes.HasFlag(FileAttributes.Directory))
        {
            return TryDelete(attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(paths.Root) : new FileInfo(paths.Root));
        }

        var root = new DirectoryInfo(paths.Root);
        var removed = RemoveContents(root, keepSettings ? paths.SettingsFile : null);
        return keepSettings ? removed : TryDelete(root) && removed;
    }

    private static bool RemoveContents(DirectoryInfo folder, string? keep)
    {
        var removed = true;
        try
        {
            foreach (var entry in folder.EnumerateFileSystemInfos())
            {
                if (keep is not null && entry is FileInfo && string.Equals(entry.FullName, keep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // A link is removed itself, without going into what it leads to.
                removed &= entry is DirectoryInfo subfolder && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    ? RemoveContents(subfolder, keep: null) && TryDelete(subfolder)
                    : TryDelete(entry);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return removed;
    }

    private static bool TryDelete(FileSystemInfo entry)
    {
        try
        {
            entry.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
