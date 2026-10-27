using System;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// The paths of folders as the settings give them, read without the disk.
/// </summary>
public static class FolderPaths
{
    // What can't be in a path Folder Rules uses: wildcards, besides what Windows doesn't allow.
    private static readonly char[] InvalidCharacters = [.. Path.GetInvalidPathChars(), '*', '?'];

    // Paths that reach Windows' devices directly, past the checks that Windows makes of a path.
    private static readonly string[] DevicePrefixes = [@"\\?\", @"\\.\", @"\??\"];

    /// <summary>
    /// The full path of <paramref name="path"/>, with <c>..</c> resolved, <c>\</c> between the folders
    /// and no <c>\</c> at the end except at a drive's root, or why it isn't one.
    /// </summary>
    public static FolderProblem Normalize(string path, out string fullPath)
    {
        ArgumentNullException.ThrowIfNull(path);

        fullPath = string.Empty;
        if (path.Trim().Length == 0)
        {
            return FolderProblem.NoPath;
        }

        if (path.IndexOfAny(InvalidCharacters) >= 0 || DevicePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return FolderProblem.InvalidPath;
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return FolderProblem.NotFullPath;
        }

        // A colon after the drive letter's would name a stream of a file, not a folder.
        if (path.IndexOf(':', 2) >= 0)
        {
            return FolderProblem.InvalidPath;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        // A network path names a server and a folder it shares, such as \\server\share.
        if (full.StartsWith(@"\\", StringComparison.Ordinal)
            && full[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries) is var parts
            && (parts.Length < 2 || parts[..2].Any(part => part is "." or "..")))
        {
            return FolderProblem.NotFullPath;
        }

        fullPath = full;
        return FolderProblem.None;
    }

    /// <summary>
    /// The folder that a destination starts with: the part before its first subfolder made from
    /// the file, such as <c>D:\Invoices</c> for <c>D:\Invoices\{year}</c>.
    /// </summary>
    public static string FixedPart(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var brace = destination.IndexOfAny(['{', '}']);
        if (brace < 0)
        {
            return destination;
        }

        var separator = destination.LastIndexOfAny(['\\', '/'], brace);
        return separator < 0 ? string.Empty : destination[..(separator + 1)];
    }

    /// <summary>Whether <paramref name="path"/> has parts made from the file, in braces.</summary>
    public static bool HasParts(string path) => path.IndexOfAny(['{', '}']) >= 0;

    /// <summary>
    /// Whether the full path <paramref name="path"/> is <paramref name="folder"/> or in it, ignoring
    /// case. A folder that isn't a full path contains nothing.
    /// </summary>
    public static bool IsInside(string path, string folder)
    {
        if (Normalize(folder, out var fullFolder) != FolderProblem.None)
        {
            return false;
        }

        return string.Equals(path, fullFolder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.EndsInDirectorySeparator(fullFolder) ? fullFolder : fullFolder + '\\', StringComparison.OrdinalIgnoreCase);
    }
}
