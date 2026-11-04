using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What Windows tells about a drive with a letter and its device, read without admin rights and
/// without opening its files: through the volume's root and a handle to the volume opened without
/// access.
/// </summary>
/// <param name="Volume">The volume's GUID path, such as <c>\\?\Volume{…}\</c>, or an empty text when Windows doesn't give it.</param>
/// <param name="Removable">Windows reports the drive as removable (<c>DRIVE_REMOVABLE</c>).</param>
/// <param name="Fixed">Windows reports the drive as fixed (<c>DRIVE_FIXED</c>).</param>
/// <param name="IsLocked">BitLocker keeps it locked: its label, file system, serial number and sizes can't be read.</param>
/// <param name="Serial">The volume serial number, which Windows gives the drive when it's formatted.</param>
/// <param name="NamedStreams">It keeps a file's other streams, such as its Mark of the Web.</param>
/// <param name="ReadOnly">The volume can't be written to.</param>
/// <param name="BusType">The device's bus, as <c>STORAGE_BUS_TYPE</c>, or 0 when unknown.</param>
/// <param name="HardwareSerial">The device's hardware serial number, or <see langword="null"/> when it reports none.</param>
/// <param name="Ejectable">The device, or one it's connected through, has Windows' removable flag, as <b>Safely Remove Hardware</b> lists.</param>
/// <param name="IsWindowsDrive">The drive Windows itself is on.</param>
public sealed record DriveReading(
    string Volume,
    bool Removable,
    bool Fixed,
    bool IsLocked,
    string Label,
    string FileSystem,
    uint Serial,
    bool NamedStreams,
    bool ReadOnly,
    long Size,
    long Free,
    int BusType,
    string? HardwareSerial,
    bool Ejectable,
    bool IsWindowsDrive);

/// <summary>What happened to a request to eject a drive.</summary>
/// <param name="Vetoed">A program, a service or the device itself has the drive in use, or the device can't be ejected this way.</param>
/// <param name="ErrorCode">Windows' <c>CONFIGRET</c>, 0 when the drive was ejected.</param>
public sealed record EjectResult(bool Ejected, bool Vetoed, int ErrorCode);

/// <summary>Reads drives with a letter and their devices, and ejects them, as <b>Safely Remove Hardware</b> does.</summary>
public static class DriveDevices
{
    /// <summary>The error a locked BitLocker volume gives (<c>FVE_E_LOCKED_VOLUME</c>).</summary>
    public const int LockedVolume = unchecked((int)0x80310000);

    private const int NamedStreamsFlag = 0x00040000;
    private const int ReadOnlyVolumeFlag = 0x00080000;

    // The longest a volume's label or file system's name is, with its null, as Windows' documentation asks.
    private const int MaximumNameLength = 261;

    // The longest a volume's GUID path is, with its null.
    private const int VolumeNameLength = 50;

    private const int DescriptorBufferSize = 1024;

    /// <summary>The letters of the drives Windows has now, as a mask: bit 0 for A, bit 25 for Z.</summary>
    public static uint Letters => PInvoke.GetLogicalDrives();

    /// <summary>
    /// Reads the drive with <paramref name="letter"/>. Returns <see langword="null"/> when there's
    /// none, or it's a card reader without a card. May wait for the drive, so don't call it on the
    /// host's UI thread.
    /// </summary>
    public static unsafe DriveReading? Read(char letter)
    {
        var root = RootOf(letter);
        var type = PInvoke.GetDriveType(root);
        if (type is PInvoke.DRIVE_UNKNOWN or PInvoke.DRIVE_NO_ROOT_DIR)
        {
            return null;
        }

        Span<char> volume = stackalloc char[VolumeNameLength];
        var volumeName = PInvoke.GetVolumeNameForVolumeMountPoint(root, volume) ? Text(volume) : string.Empty;

        Span<char> label = stackalloc char[MaximumNameLength];
        Span<char> fileSystem = stackalloc char[MaximumNameLength];
        bool locked;
        if (PInvoke.GetVolumeInformation(root, label, out var serial, out _, out var flags, fileSystem))
        {
            locked = false;
        }
        else
        {
            // A card reader without a card isn't ready; a locked BitLocker volume says so.
            if (Marshal.GetLastPInvokeError() != LockedVolume)
            {
                return null;
            }

            locked = true;
            (serial, flags) = (0, 0);
            label.Clear();
            fileSystem.Clear();
        }

        long size = 0;
        long free = 0;
        if (!locked && PInvoke.GetDiskFreeSpaceEx(root, out var freeToCaller, out var total, out _))
        {
            (size, free) = ((long)total, (long)freeToCaller);
        }

        var device = ReadDevice(letter);
        return new DriveReading(
            volumeName,
            type == PInvoke.DRIVE_REMOVABLE,
            type == PInvoke.DRIVE_FIXED,
            locked,
            Text(label),
            Text(fileSystem),
            serial,
            ((int)flags & NamedStreamsFlag) != 0,
            ((int)flags & ReadOnlyVolumeFlag) != 0,
            size,
            free,
            device.BusType,
            device.Serial,
            device.EjectableNode is not null,
            string.Equals(Path.GetPathRoot(Environment.SystemDirectory), root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Asks Windows to eject the drive with <paramref name="letter"/>: the device it's on, or the
    /// one it's connected through, that Windows lets the user eject. Programs that registered for
    /// it are asked first, and any of them can refuse. Waits for the eject, so don't call it on the
    /// host's UI thread.
    /// </summary>
    public static unsafe EjectResult Eject(char letter)
    {
        // Never the drive Windows itself is on, even when it's on a device Windows could eject.
        if (string.Equals(Path.GetPathRoot(Environment.SystemDirectory), RootOf(letter), StringComparison.OrdinalIgnoreCase)
            || ReadDevice(letter).EjectableNode is not { } node)
        {
            return new EjectResult(Ejected: false, Vetoed: true, (int)CONFIGRET.CR_NOT_DISABLEABLE);
        }

        PNP_VETO_TYPE veto;
        var result = PInvoke.CM_Request_Device_Eject(node, &veto, null, 0, 0);
        return new EjectResult(result == CONFIGRET.CR_SUCCESS, result == CONFIGRET.CR_REMOVE_VETOED, (int)result);
    }

    private static string RootOf(char letter)
    {
        if (!char.IsAsciiLetter(letter))
        {
            throw new ArgumentOutOfRangeException(nameof(letter), letter, "Not a drive letter.");
        }

        return $"{char.ToUpperInvariant(letter)}:\\";
    }

    private static string Text(ReadOnlySpan<char> buffer)
    {
        var end = buffer.IndexOf('\0');
        return (end < 0 ? buffer : buffer[..end]).ToString();
    }

    // The device's bus and serial number, through a handle to the volume opened without access,
    // which needs no admin rights, and the device Windows lets the user eject.
    private static unsafe (int BusType, string? Serial, uint? EjectableNode) ReadDevice(char letter)
    {
        using var volume = PInvoke.CreateFile(
            $@"\\.\{char.ToUpperInvariant(letter)}:",
            0,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            0,
            null);
        if (volume.IsInvalid)
        {
            return (0, null, null);
        }

        var (busType, serial) = ReadDescriptor(volume);
        uint? node = null;
        STORAGE_DEVICE_NUMBER number = default;
        if (PInvoke.DeviceIoControl((HANDLE)volume.DangerousGetHandle(), PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &number, (uint)sizeof(STORAGE_DEVICE_NUMBER), null, null))
        {
            node = EjectableNodeOf(number.DeviceNumber);
        }

        return (busType, serial, node);
    }

    private static unsafe (int BusType, string? Serial) ReadDescriptor(SafeFileHandle volume)
    {
        var query = new STORAGE_PROPERTY_QUERY { PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty, QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery };
        var buffer = stackalloc byte[DescriptorBufferSize];
        uint returned = 0;
        if (!PInvoke.DeviceIoControl((HANDLE)volume.DangerousGetHandle(), PInvoke.IOCTL_STORAGE_QUERY_PROPERTY, &query, (uint)sizeof(STORAGE_PROPERTY_QUERY), buffer, DescriptorBufferSize, &returned, null)
            || returned < sizeof(STORAGE_DEVICE_DESCRIPTOR))
        {
            return (0, null);
        }

        var descriptor = (STORAGE_DEVICE_DESCRIPTOR*)buffer;
        var serialOffset = descriptor->SerialNumberOffset;
        string? serial = null;
        if (serialOffset != 0 && serialOffset < returned)
        {
            var length = new ReadOnlySpan<byte>(buffer + serialOffset, (int)(returned - serialOffset)).IndexOf((byte)0);
            serial = Encoding.ASCII.GetString(buffer + serialOffset, length < 0 ? (int)(returned - serialOffset) : length).Trim();
            if (serial.Length == 0)
            {
                serial = null;
            }
        }

        return ((int)descriptor->BusType, serial);
    }

    // The disk with the device number, then it and the devices it's connected through, up to the
    // first one with Windows' removable flag, which is the one Safely Remove Hardware ejects.
    private static unsafe uint? EjectableNodeOf(uint deviceNumber)
    {
        var diskClass = PInvoke.GUID_DEVINTERFACE_DISK;
        if (PInvoke.CM_Get_Device_Interface_List_Size(out var length, diskClass, null, CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS || length <= 1)
        {
            return null;
        }

        var list = new char[length];
        fixed (char* listPointer = list)
        {
            if (PInvoke.CM_Get_Device_Interface_List(in diskClass, default, new PZZWSTR(listPointer), length, CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
            {
                return null;
            }
        }

        foreach (var path in new string(list).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (DiskNumberOf(path) == deviceNumber && InstanceOf(path) is { } instance && Locate(instance) is { } node)
            {
                return EjectableAncestor(node);
            }
        }

        return null;
    }

    private static unsafe uint? Locate(string instance)
    {
        fixed (char* id = instance + "\0")
        {
            return PInvoke.CM_Locate_DevNode(out var node, new PWSTR(id), CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL) == CONFIGRET.CR_SUCCESS ? node : null;
        }
    }

    private static unsafe uint? DiskNumberOf(string interfacePath)
    {
        using var disk = PInvoke.CreateFile(
            interfacePath,
            0,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            0,
            null);
        STORAGE_DEVICE_NUMBER number = default;
        return !disk.IsInvalid && PInvoke.DeviceIoControl((HANDLE)disk.DangerousGetHandle(), PInvoke.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &number, (uint)sizeof(STORAGE_DEVICE_NUMBER), null, null)
            ? number.DeviceNumber
            : null;
    }

    private static unsafe string? InstanceOf(string interfacePath)
    {
        var buffer = new byte[1024];
        var size = (uint)buffer.Length;
        return PInvoke.CM_Get_Device_Interface_Property(interfacePath, PInvoke.DEVPKEY_Device_InstanceId, out _, buffer, ref size, 0) == CONFIGRET.CR_SUCCESS
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0')
            : null;
    }

    private static unsafe uint? EjectableAncestor(uint node)
    {
        const uint Removable = 0x4; // CM_DEVCAP_REMOVABLE
        for (var current = node; ;)
        {
            uint capabilities = 0;
            var size = (uint)sizeof(uint);
            if (PInvoke.CM_Get_DevNode_Registry_Property(current, PInvoke.CM_DRP_CAPABILITIES, out _, MemoryMarshal.AsBytes(new Span<uint>(ref capabilities)), ref size, 0) == CONFIGRET.CR_SUCCESS
                && (capabilities & Removable) != 0)
            {
                return current;
            }

            if (PInvoke.CM_Get_Parent(out var parent, current, 0) != CONFIGRET.CR_SUCCESS)
            {
                return null;
            }

            current = parent;
        }
    }
}
