using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>Whether a picture or folder can be used now.</summary>
public enum PictureState
{
    Usable,

    /// <summary>It isn't there.</summary>
    Missing,

    /// <summary>It's on a network location, such as a mapped network drive.</summary>
    OnNetwork,

    /// <summary>A cloud storage app keeps it online only, so reading it would download it.</summary>
    OnlineOnly,
}

/// <summary>
/// Checks pictures and lists folders without reading any picture: only names, sizes and
/// attributes. See "Pictures" in docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public interface IPictureFiles
{
    /// <summary>Whether a picture, a full path, can be used now.</summary>
    PictureState Check(string picture);

    /// <summary>Whether a folder, a full path, can be used now.</summary>
    PictureState CheckFolder(string folder);

    /// <summary>
    /// The pictures in a folder that can be used now, without its subfolders, by name, at most
    /// <see cref="WindowsPictureFiles.MaximumFolderPictures"/>; none when the folder can't be read.
    /// </summary>
    IReadOnlyList<string> ListFolder(string folder);
}

/// <summary>Pictures and folders on this PC's drives. Call it off the UI thread: it reads the disk.</summary>
public sealed class WindowsPictureFiles : IPictureFiles
{
    /// <summary>The most pictures of a folder that count, the first ones by name.</summary>
    public const int MaximumFolderPictures = 10_000;

    // Files that a cloud storage app keeps online only (FILE_ATTRIBUTE_RECALL_ON_OPEN and
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS), besides offline ones.
    private const FileAttributes OnlineOnly = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    public PictureState Check(string picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        if (IsOnNetwork(picture))
        {
            return PictureState.OnNetwork;
        }

        try
        {
            var attributes = File.GetAttributes(picture);
            return attributes.HasFlag(FileAttributes.Directory) ? PictureState.Missing
                : (attributes & OnlineOnly) != 0 ? PictureState.OnlineOnly
                : PictureState.Usable;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PictureState.Missing;
        }
    }

    public PictureState CheckFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return IsOnNetwork(folder) ? PictureState.OnNetwork
            : Directory.Exists(folder) ? PictureState.Usable
            : PictureState.Missing;
    }

    public IReadOnlyList<string> ListFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (CheckFolder(folder) != PictureState.Usable)
        {
            return [];
        }

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory | OnlineOnly,
            };
            return [.. Directory.EnumerateFiles(folder, "*", options)
                .Where(DynamicWallpaperCheck.IsPicture)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumFolderPictures)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    // A drive letter that stands for a network location; a network path is already refused.
    private static bool IsOnNetwork(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }
    }
}
