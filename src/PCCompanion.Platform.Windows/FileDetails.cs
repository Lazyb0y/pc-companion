using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What Windows keeps about a file or folder, read without opening its contents, so that a file
/// kept online only isn't downloaded and a link isn't followed.
/// </summary>
/// <param name="Name">The name, without the folder.</param>
/// <param name="ReparseTag">What kind of reparse point it is, such as a link, or 0.</param>
/// <param name="Changed">When it was last changed, moved or renamed; 0 on drives that don't keep it, such as FAT.</param>
/// <param name="FileId">The volume's serial number and the file's ID, or <see langword="null"/> on drives without lasting IDs.</param>
public sealed record FileDetails(
    string Name,
    FileAttributes Attributes,
    uint ReparseTag,
    long Size,
    DateTimeOffset Created,
    DateTimeOffset Modified,
    DateTimeOffset Changed,
    string? FileId)
{
    // Kept online by a cloud storage app: reading the file would download it.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    public bool IsFolder => Attributes.HasFlag(FileAttributes.Directory);

    /// <summary>
    /// Whether it's a symbolic link, a junction or another reparse point that stands for something
    /// elsewhere, which Windows marks as a name surrogate.
    /// </summary>
    public bool IsLink => Attributes.HasFlag(FileAttributes.ReparsePoint) && (ReparseTag & 0x20000000) != 0;

    /// <summary>Whether a cloud storage app keeps it online only, so that reading it would download it.</summary>
    public bool IsOnlineOnly => (Attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    /// <summary>
    /// Reads a file or folder's details through a handle that only reads its attributes, and
    /// doesn't follow a link. Returns <see langword="null"/> when it doesn't exist.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused to read it, for another reason than that it doesn't exist.</exception>
    public static unsafe FileDetails? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var handle = PInvoke.CreateFile(
            path,
            (uint)FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            return error is (int)WIN32_ERROR.ERROR_FILE_NOT_FOUND or (int)WIN32_ERROR.ERROR_PATH_NOT_FOUND ? null : throw new Win32Exception(error);
        }

        FILE_BASIC_INFO basic;
        FILE_STANDARD_INFO standard;
        FILE_ATTRIBUTE_TAG_INFO tag;
        Query(handle, FILE_INFO_BY_HANDLE_CLASS.FileBasicInfo, &basic, sizeof(FILE_BASIC_INFO));
        Query(handle, FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo, &standard, sizeof(FILE_STANDARD_INFO));
        Query(handle, FILE_INFO_BY_HANDLE_CLASS.FileAttributeTagInfo, &tag, sizeof(FILE_ATTRIBUTE_TAG_INFO));

        return new FileDetails(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            (FileAttributes)basic.FileAttributes,
            tag.ReparseTag,
            standard.EndOfFile,
            Time(basic.CreationTime),
            Time(basic.LastWriteTime),
            Time(basic.ChangeTime),
            ReadFileId(handle));
    }

    /// <summary>
    /// Whether the file opens for reading without letting other programs write to it, which fails
    /// while a program has it open for writing. Any other failure, such as a file that's gone,
    /// counts as alone, so that what the rules do next reports it. Don't call it for a file kept
    /// online only, which reading would download.
    /// </summary>
    public static unsafe bool CanOpenAlone(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var handle = PInvoke.CreateFile(
            path,
            (uint)FILE_ACCESS_RIGHTS.FILE_READ_DATA,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OPEN_REPARSE_POINT,
            null);
        return !handle.IsInvalid
            || Marshal.GetLastPInvokeError() is not ((int)WIN32_ERROR.ERROR_SHARING_VIOLATION or (int)WIN32_ERROR.ERROR_LOCK_VIOLATION);
    }

    /// <summary>
    /// Lists the files and folders directly in a folder, with their details, in one pass over the
    /// folder. The folder's links aren't followed.
    /// </summary>
    /// <exception cref="Win32Exception">The folder can't be read.</exception>
    public static unsafe IReadOnlyList<FileDetails> List(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        using var handle = PInvoke.CreateFile(
            folder,
            (uint)FILE_ACCESS_RIGHTS.FILE_LIST_DIRECTORY,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var volume = ReadVolumeSerial(handle);
        var items = new List<FileDetails>();
        const int BufferSize = 64 * 1024;
        var buffer = (byte*)NativeMemory.Alloc(BufferSize);
        try
        {
            // Drives without lasting file IDs, such as FAT, don't list them; their listing has the rest.
            var withIds = volume is not null;
            var restart = true;
            while (true)
            {
                var kind = (withIds, restart) switch
                {
                    (true, true) => FILE_INFO_BY_HANDLE_CLASS.FileIdExtdDirectoryRestartInfo,
                    (true, false) => FILE_INFO_BY_HANDLE_CLASS.FileIdExtdDirectoryInfo,
                    (false, true) => FILE_INFO_BY_HANDLE_CLASS.FileFullDirectoryRestartInfo,
                    (false, false) => FILE_INFO_BY_HANDLE_CLASS.FileFullDirectoryInfo,
                };

                if (!PInvoke.GetFileInformationByHandleEx((HANDLE)handle.DangerousGetHandle(), kind, buffer, BufferSize))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
                    {
                        break;
                    }

                    if (withIds && restart && error is (int)WIN32_ERROR.ERROR_INVALID_PARAMETER or (int)WIN32_ERROR.ERROR_NOT_SUPPORTED)
                    {
                        withIds = false;
                        continue;
                    }

                    throw new Win32Exception(error);
                }

                restart = false;
                for (var entry = buffer; ;)
                {
                    var item = withIds ? FromIdEntry((FILE_ID_EXTD_DIR_INFO*)entry, volume!) : FromFullEntry((FILE_FULL_DIR_INFO*)entry);
                    if (item.Name is not ("." or ".."))
                    {
                        items.Add(item);
                    }

                    var next = *(uint*)entry;
                    if (next == 0)
                    {
                        break;
                    }

                    entry += next;
                }
            }
        }
        finally
        {
            NativeMemory.Free(buffer);
        }

        return items;
    }

    private static unsafe FileDetails FromIdEntry(FILE_ID_EXTD_DIR_INFO* entry, string volume) => new(
        new string((char*)&entry->FileName, 0, (int)entry->FileNameLength / sizeof(char)),
        (FileAttributes)entry->FileAttributes,
        entry->ReparsePointTag,
        entry->EndOfFile,
        Time(entry->CreationTime),
        Time(entry->LastWriteTime),
        Time(entry->ChangeTime),
        FileIdText(volume, entry->FileId));

    // A reparse point's tag is in EaSize, as in a listing of FindFirstFile.
    private static unsafe FileDetails FromFullEntry(FILE_FULL_DIR_INFO* entry) => new(
        new string((char*)&entry->FileName, 0, (int)entry->FileNameLength / sizeof(char)),
        (FileAttributes)entry->FileAttributes,
        ((FileAttributes)entry->FileAttributes).HasFlag(FileAttributes.ReparsePoint) ? entry->EaSize : 0,
        entry->EndOfFile,
        Time(entry->CreationTime),
        Time(entry->LastWriteTime),
        Time(entry->ChangeTime),
        null);

    private static unsafe void Query(SafeFileHandle handle, FILE_INFO_BY_HANDLE_CLASS kind, void* info, int size)
    {
        if (!PInvoke.GetFileInformationByHandleEx((HANDLE)handle.DangerousGetHandle(), kind, info, (uint)size))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    // FAT and exFAT drives and some servers have no lasting file IDs, and refuse to give them.
    private static unsafe string? ReadFileId(SafeFileHandle handle)
    {
        FILE_ID_INFO id;
        return PInvoke.GetFileInformationByHandleEx((HANDLE)handle.DangerousGetHandle(), FILE_INFO_BY_HANDLE_CLASS.FileIdInfo, &id, (uint)sizeof(FILE_ID_INFO))
            ? FileIdText(id.VolumeSerialNumber.ToString("X16", CultureInfo.InvariantCulture), id.FileId)
            : null;
    }

    private static unsafe string? ReadVolumeSerial(SafeFileHandle handle)
    {
        FILE_ID_INFO id;
        return PInvoke.GetFileInformationByHandleEx((HANDLE)handle.DangerousGetHandle(), FILE_INFO_BY_HANDLE_CLASS.FileIdInfo, &id, (uint)sizeof(FILE_ID_INFO))
            ? id.VolumeSerialNumber.ToString("X16", CultureInfo.InvariantCulture)
            : null;
    }

    private static unsafe string FileIdText(string volume, FILE_ID_128 id) =>
        volume + "-" + Convert.ToHexString(new ReadOnlySpan<byte>(&id.Identifier, 16));

    private static DateTimeOffset Time(long fileTime) =>
        fileTime <= 0 ? DateTimeOffset.FromFileTime(0) : DateTimeOffset.FromFileTime(fileTime);
}
