namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>What kind of drive a connected drive is, as the page names it.</summary>
public enum ConnectedDriveKind
{
    /// <summary>A drive Windows treats as removable, such as a USB flash drive or a card reader.</summary>
    UsbDrive,

    /// <summary>A memory card in the PC's own card slot.</summary>
    MemoryCard,

    /// <summary>A USB or Thunderbolt disk that Windows calls fixed but lets the user eject.</summary>
    ExternalDisk,

    /// <summary>A virtual disk, VHD or VHDX, that the user attached.</summary>
    VirtualDisk,
}

/// <summary>How Windows sees a drive's volume and its device, which tells whether the drive counts.</summary>
/// <param name="Removable">Windows reports the drive as removable (<c>DRIVE_REMOVABLE</c>).</param>
/// <param name="Fixed">Windows reports the drive as fixed (<c>DRIVE_FIXED</c>); neither means a network drive, a CD or DVD, or a RAM disk.</param>
/// <param name="BusType">The device's bus, as <c>STORAGE_BUS_TYPE</c>: 12 SD, 13 MMC, 15 a file-backed virtual disk.</param>
/// <param name="Ejectable">The device, or one it's connected through, has Windows' removable flag (<c>CM_DEVCAP_REMOVABLE</c>).</param>
/// <param name="IsWindowsDrive">The drive Windows itself is on.</param>
public sealed record DriveDevice(bool Removable, bool Fixed, int BusType, bool Ejectable, bool IsWindowsDrive)
{
    private const int SdBus = 12;
    private const int MmcBus = 13;
    private const int FileBackedVirtualBus = 15;

    /// <summary>
    /// The drive's kind, or <see langword="null"/> when it doesn't count, as the PC's own disks,
    /// network drives, CDs and RAM disks don't. See "Which drives count" in
    /// docs/features/files/drive-actions.md.
    /// </summary>
    public ConnectedDriveKind? Kind => this switch
    {
        { IsWindowsDrive: true } => null,
        { BusType: FileBackedVirtualBus } when Removable || Fixed => ConnectedDriveKind.VirtualDisk,
        { BusType: SdBus or MmcBus } when Removable || Fixed => ConnectedDriveKind.MemoryCard,
        { Removable: true } => ConnectedDriveKind.UsbDrive,
        { Fixed: true, Ejectable: true } => ConnectedDriveKind.ExternalDisk,
        _ => null,
    };
}

/// <summary>
/// What PC Companion read about a connected drive, without its files: enough to recognize it and
/// to say on the page what it is.
/// </summary>
/// <param name="Volume">The volume's GUID path, such as <c>\\?\Volume{…}\</c>, which names it while it's connected.</param>
/// <param name="Letter">Its drive letter.</param>
/// <param name="Kind">Its kind, or <see langword="null"/> when it doesn't count.</param>
/// <param name="IsLocked">BitLocker keeps it locked, so nothing else could be read.</param>
/// <param name="HardwareSerial">The device's hardware serial number, or <see langword="null"/> when it has none.</param>
/// <param name="KeepsStreams">It keeps a file's other streams, such as its Mark of the Web.</param>
/// <param name="IsReadOnly">Nothing can be written to it, as on a card with its lock switch on.</param>
/// <param name="HasDcim">It has a folder named <c>DCIM</c> at its root that isn't a link.</param>
/// <param name="IdFileText">The text of its ID file, or <see langword="null"/> when it has none or it can't be read.</param>
public sealed record ConnectedDrive(
    string Volume,
    char Letter,
    ConnectedDriveKind? Kind,
    bool IsLocked,
    string Label,
    string FileSystem,
    uint VolumeSerial,
    string? HardwareSerial,
    long Size,
    long Free,
    bool KeepsStreams,
    bool IsReadOnly,
    bool HasDcim,
    string? IdFileText)
{
    /// <summary>The root folder, such as <c>E:\</c>.</summary>
    public string Root => $"{Letter}:\\";

    /// <summary>
    /// The largest file it can hold: just under 4 GB on FAT and FAT32 drives, whose files keep their
    /// size in 32 bits; no limit on the others.
    /// </summary>
    public long MaximumFileSize =>
        FileSystem.Equals("FAT32", System.StringComparison.OrdinalIgnoreCase) || FileSystem.Equals("FAT", System.StringComparison.OrdinalIgnoreCase)
            ? uint.MaxValue
            : long.MaxValue;
}
