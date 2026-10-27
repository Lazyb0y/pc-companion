using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace PCCompanion.Platform.Windows;

/// <summary>What kind of drive a folder is on.</summary>
public enum DriveKind
{
    /// <summary>One of the PC's own drives, including a USB hard drive.</summary>
    Fixed,

    /// <summary>A drive that Windows treats as removable, such as a USB flash drive or a memory card.</summary>
    Removable,

    /// <summary>A network location, by drive letter or by a path such as <c>\\server\share</c>.</summary>
    Network,

    /// <summary>Another kind, such as a CD or a RAM disk.</summary>
    Other,
}

/// <summary>What the drive a folder is on can keep.</summary>
/// <param name="FileSystem">The file system's name, such as <c>NTFS</c> or <c>exFAT</c>.</param>
/// <param name="NamedStreams">
/// Whether it keeps a file's other streams, such as its Mark of the Web, which Windows drops
/// without an error when a file is moved or copied to a drive that doesn't.
/// </param>
public sealed record VolumeDetails(DriveKind Kind, string FileSystem, bool NamedStreams)
{
    private const uint NamedStreamsFlag = 0x00040000;

    // The longest file system name Windows gives, with its null, as its documentation asks.
    private const int MaximumNameLength = 261;

    /// <summary>Reads the details of the drive that <paramref name="folder"/> is on.</summary>
    /// <exception cref="Win32Exception">The folder can't be reached.</exception>
    public static VolumeDetails Read(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        using var handle = PInvoke.CreateFile(
            folder,
            (uint)FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Span<char> name = stackalloc char[MaximumNameLength];
        if (!PInvoke.GetVolumeInformationByHandle(handle, [], out _, out _, out var flags, name))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new VolumeDetails(KindOf(folder), name[..name.IndexOf('\0')].ToString(), (flags & NamedStreamsFlag) != 0);
    }

    /// <summary>
    /// What kind of drive a folder's path is on, read from its drive letter or its server name,
    /// without reaching the folder, so it also works while a network folder can't be reached.
    /// </summary>
    public static DriveKind KindOf(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        if (folder.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return DriveKind.Network;
        }

        return PInvoke.GetDriveType(Path.GetPathRoot(folder)) switch
        {
            PInvoke.DRIVE_FIXED => DriveKind.Fixed,
            PInvoke.DRIVE_REMOVABLE => DriveKind.Removable,
            PInvoke.DRIVE_REMOTE => DriveKind.Network,
            _ => DriveKind.Other,
        };
    }
}
