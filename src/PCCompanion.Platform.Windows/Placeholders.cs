using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.CloudFilters;
using Windows.Win32.Storage.FileSystem;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A file or folder for Windows to show in a sync root before it's downloaded: its name, the
/// provider's identity for it, and what the folder shows of it.
/// </summary>
/// <param name="Name">Its name, without a folder.</param>
/// <param name="Identity">What the provider needs to find it again, at most 4 KB; Windows gives it back with every request.</param>
public sealed record PlaceholderEntry(string Name, byte[] Identity, bool IsDirectory, long Size, DateTime LastWriteTimeUtc)
{
    /// <summary>The most bytes of an identity that Windows keeps.</summary>
    public const int MaximumIdentityLength = 4 * 1024;

    /// <summary>Whether File Explorer shows it only with Hidden items on.</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether it's marked as the same as on the server, as it is when made from the server's listing.</summary>
    public bool InSync { get; init; } = true;
}

/// <summary>Whether a placeholder's copy stays on the PC, as File Explorer's menu sets it.</summary>
public enum PinState
{
    /// <summary>Neither: Windows may remove the copy, and the provider downloads it when a program reads it.</summary>
    Unspecified,

    /// <summary>Always keep on this device.</summary>
    Pinned,

    /// <summary>Free up space.</summary>
    Unpinned,

    /// <summary>Never synced, as a program's temporary file.</summary>
    Excluded,

    /// <summary>The folder's.</summary>
    Inherit,
}

/// <summary>A placeholder's state, as the Cloud Files API reports it.</summary>
/// <param name="OnDiskSize">How many of its bytes are on the PC.</param>
/// <param name="ModifiedSize">How many bytes programs changed since it was last marked in sync.</param>
public sealed record PlaceholderState(bool InSync, PinState Pin, long OnDiskSize, long ModifiedSize, IReadOnlyList<byte> Identity, FileAttributes Attributes)
{
    /// <summary>Whether all of it is on the PC: Windows' "always available" or "available on this device".</summary>
    public bool IsDownloaded => (Attributes & (FileAttributes)0x00400000) == 0;
}

/// <summary>What <see cref="Placeholders.Update"/> does besides changing the metadata or identity.</summary>
[Flags]
public enum PlaceholderUpdate
{
    None = 0,

    /// <summary>Fails with <c>ERROR_CLOUD_FILE_NOT_IN_SYNC</c> when the placeholder has changes, so they're never lost.</summary>
    VerifyInSync = 0x1,

    MarkInSync = 0x2,

    /// <summary>Removes the copy on the PC.</summary>
    Dehydrate = 0x4,
}

/// <summary>
/// Placeholders in a sync root: making them, reading their state, and changing it. Every method
/// returns Windows' HRESULT, 0 for success, and touches only the path it's given.
/// </summary>
public static class Placeholders
{
    // HRESULT_FROM_WIN32(ERROR_NOT_A_CLOUD_FILE)
    private const int NotACloudFile = unchecked((int)0x80070178);

    // E_ACCESSDENIED
    private const int AccessDenied = unchecked((int)0x80070005);

    // HRESULT_FROM_WIN32(ERROR_MORE_DATA): the identity didn't fit, which a 4 KB buffer rules out.
    private const int MoreData = unchecked((int)0x800700EA);

    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: not all of the file is on the PC.
    private const uint RecallOnDataAccess = 0x00400000;

    /// <summary>
    /// Makes placeholders in <paramref name="folder"/>, which must be in a connected sync root.
    /// Each entry's result goes in <paramref name="results"/>, as long as <paramref name="entries"/>;
    /// an entry whose name is taken gets <c>ERROR_ALREADY_EXISTS</c> and is left as it is.
    /// </summary>
    public static unsafe int Create(string folder, IReadOnlyList<PlaceholderEntry> entries, Span<int> results)
    {
        if (entries.Count == 0)
        {
            return 0;
        }

        using var native = new NativePlaceholders(entries);
        int result = PInvoke.CfCreatePlaceholders(folder, native.Infos, CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE, out _).Value;
        native.CopyResults(results);
        return result;
    }

    /// <summary>The placeholder's state, or <see langword="null"/> when <paramref name="path"/> isn't one, such as a new file.</summary>
    /// <exception cref="IOException">It can't be opened, for example because it's gone.</exception>
    public static unsafe PlaceholderState? Read(string path)
    {
        using var handle = OpenForAttributes(path);
        Span<byte> buffer = stackalloc byte[sizeof(CF_PLACEHOLDER_STANDARD_INFO) + PlaceholderEntry.MaximumIdentityLength];
        int result = PInvoke.CfGetPlaceholderInfo(handle, CF_PLACEHOLDER_INFO_CLASS.CF_PLACEHOLDER_INFO_STANDARD, buffer, out _).Value;
        if (result == NotACloudFile)
        {
            return null;
        }

        if (result != 0 && result != MoreData)
        {
            throw new IOException($"The placeholder's state can't be read (0x{result:X8}).", result);
        }

        fixed (byte* bytes = buffer)
        {
            var info = (CF_PLACEHOLDER_STANDARD_INFO*)bytes;
            int length = (int)Math.Min(info->FileIdentityLength, (uint)PlaceholderEntry.MaximumIdentityLength);
            byte[] identity = new ReadOnlySpan<byte>(bytes + Marshal.OffsetOf<CF_PLACEHOLDER_STANDARD_INFO>(nameof(CF_PLACEHOLDER_STANDARD_INFO.FileIdentity)), length).ToArray();
            return new PlaceholderState(
                info->InSyncState == CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC,
                (PinState)info->PinState,
                info->OnDiskDataSize,
                info->ModifiedDataSize,
                identity,
                File.GetAttributes(path));
        }
    }

    /// <summary>Marks a placeholder as the same as on the server, or not.</summary>
    public static int SetInSync(string path, bool inSync)
    {
        using var handle = OpenForAttributes(path);
        return PInvoke.CfSetInSyncState(handle, inSync ? CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC : CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_NOT_IN_SYNC, CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE).Value;
    }

    /// <summary>Makes a new file or folder in a sync root a placeholder with <paramref name="identity"/>, as after uploading it.</summary>
    public static int Convert(string path, byte[] identity, bool markInSync)
    {
        using var handle = OpenForAttributes(path);
        return PInvoke.CfConvertToPlaceholder(handle, identity, markInSync ? CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC : CF_CONVERT_FLAGS.CF_CONVERT_FLAG_NONE).Value;
    }

    /// <summary>
    /// Has Windows ask the provider for a folder's contents again the next time a program lists
    /// it, after an answer told Windows the folder was complete.
    /// </summary>
    public static int ListAgainOnNextOpen(string folder)
    {
        int result;
        using (var handle = OpenForAttributes(folder))
        {
            result = PInvoke.CfUpdatePlaceholder(handle, (CF_FS_METADATA?)null, default(ReadOnlySpan<byte>), default, CF_UPDATE_FLAGS.CF_UPDATE_FLAG_ENABLE_ON_DEMAND_POPULATION).Value;
        }

        if (result != AccessDenied)
        {
            return result;
        }

        // A handle that reads only attributes may not be enough; one that steps aside for programs is.
        int opened = OplockHandle.Open(folder, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_WRITE_ACCESS, out var oplock);
        if (opened != 0)
        {
            return opened;
        }

        using (oplock)
        {
            return oplock!.Use(handle => PInvoke.CfUpdatePlaceholder(handle, (CF_FS_METADATA?)null, default(ReadOnlySpan<byte>), default, CF_UPDATE_FLAGS.CF_UPDATE_FLAG_ENABLE_ON_DEMAND_POPULATION).Value);
        }
    }

    /// <summary>
    /// Makes a new folder in a sync root a placeholder with <paramref name="identity"/>, marked in
    /// sync, whose contents Windows asks the provider for each time a program lists it, as for a
    /// folder made from the server's listing.
    /// </summary>
    public static int ConvertFolder(string path, byte[] identity)
    {
        using var handle = OpenForAttributes(path);
        return PInvoke.CfConvertToPlaceholder(handle, identity, CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC | CF_CONVERT_FLAGS.CF_CONVERT_FLAG_ENABLE_ON_DEMAND_POPULATION).Value;
    }

    /// <summary>
    /// Changes a placeholder's size and dates, or its identity, or both, as after a change on the
    /// server, through a handle that only it holds, as removing its copy requires. A program that
    /// has the file open makes it fail; it's tried again later.
    /// </summary>
    /// <param name="identity">Its new identity, or <see langword="null"/> to keep it.</param>
    public static int Update(string path, PlaceholderEntry? metadata, byte[]? identity, PlaceholderUpdate update)
    {
        int opened = OplockHandle.Open(path, CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE | CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_WRITE_ACCESS, out var file);
        if (opened != 0)
        {
            return opened;
        }

        using (file)
        {
            CF_FS_METADATA? fs = metadata is null ? null : NativePlaceholders.Metadata(metadata);
            return file!.Use(handle => PInvoke.CfUpdatePlaceholder(handle, fs, identity ?? default(ReadOnlySpan<byte>), default, (CF_UPDATE_FLAGS)update).Value);
        }
    }

    /// <summary>Removes the copy of a placeholder's data on the PC, as Free up space asks.</summary>
    public static int Dehydrate(string path)
    {
        using var handle = OpenForAttributes(path);
        return PInvoke.CfDehydratePlaceholder(handle, 0, -1, CF_DEHYDRATE_FLAGS.CF_DEHYDRATE_FLAG_NONE).Value;
    }

    /// <summary>Downloads all of a placeholder's data, as Always keep on this device asks; waits until it's there.</summary>
    public static int Hydrate(string path)
    {
        using var handle = OpenForAttributes(path);
        return PInvoke.CfHydratePlaceholder(handle, 0, -1, CF_HYDRATE_FLAGS.CF_HYDRATE_FLAG_NONE).Value;
    }

    /// <summary>Sets whether a placeholder's copy stays on the PC, such as <see cref="PinState.Excluded"/> for a program's temporary file.</summary>
    public static int SetPinState(string path, PinState pin)
    {
        using var handle = OpenForAttributes(path);
        return PInvoke.CfSetPinState(handle, (CF_PIN_STATE)pin, CF_SET_PIN_FLAGS.CF_SET_PIN_FLAG_NONE).Value;
    }

    /// <summary>Whether <paramref name="attributes"/> say that not all of a file is on the PC.</summary>
    public static bool IsOnlineOnly(FileAttributes attributes) => ((uint)attributes & RecallOnDataAccess) != 0;

    // A handle that reads only attributes, which doesn't download the file, also for a folder.
    private static SafeFileHandle OpenForAttributes(string path)
    {
        var handle = PInvoke.CreateFile(
            path,
            (uint)FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"The placeholder can't be opened (error {error}).", unchecked((int)0x80070000 | error));
        }

        return handle;
    }
}

/// <summary>
/// The native form of placeholder entries for <c>CfCreatePlaceholders</c> and listings: names and
/// identities in memory that doesn't move, freed when disposed.
/// </summary>
internal sealed unsafe class NativePlaceholders : IDisposable
{
    private readonly List<nint> _memory = [];

    public NativePlaceholders(IReadOnlyList<PlaceholderEntry> entries)
    {
        Infos = new CF_PLACEHOLDER_CREATE_INFO[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Identity.Length > PlaceholderEntry.MaximumIdentityLength)
            {
                throw new ArgumentException($"An identity is {entry.Identity.Length} bytes, more than Windows keeps.", nameof(entries));
            }

            nint name = Marshal.StringToHGlobalUni(entry.Name);
            _memory.Add(name);
            nint identity = Marshal.AllocHGlobal(Math.Max(1, entry.Identity.Length));
            _memory.Add(identity);
            entry.Identity.CopyTo(new Span<byte>((void*)identity, entry.Identity.Length));

            Infos[i] = new CF_PLACEHOLDER_CREATE_INFO
            {
                RelativeFileName = (char*)name,
                FsMetadata = Metadata(entry),
                FileIdentity = (void*)identity,
                FileIdentityLength = (uint)entry.Identity.Length,
                Flags = entry.InSync ? CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC : CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_NONE,
            };
        }
    }

    public CF_PLACEHOLDER_CREATE_INFO[] Infos { get; }

    public static CF_FS_METADATA Metadata(PlaceholderEntry entry)
    {
        long time = entry.LastWriteTimeUtc.ToFileTimeUtc();
        uint attributes = entry.IsDirectory ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal;
        if (entry.Hidden)
        {
            attributes = (attributes & ~(uint)FileAttributes.Normal) | (uint)FileAttributes.Hidden;
        }

        return new CF_FS_METADATA
        {
            BasicInfo = new FILE_BASIC_INFO
            {
                CreationTime = time,
                LastAccessTime = time,
                LastWriteTime = time,
                ChangeTime = time,
                FileAttributes = attributes,
            },
            FileSize = entry.IsDirectory ? 0 : entry.Size,
        };
    }

    public void CopyResults(Span<int> results)
    {
        for (int i = 0; i < Infos.Length && i < results.Length; i++)
        {
            results[i] = Infos[i].Result.Value;
        }
    }

    public void Dispose()
    {
        foreach (nint block in _memory)
        {
            Marshal.FreeHGlobal(block);
        }

        _memory.Clear();
    }
}
