using PCCompanion.Modules.Files.DriveActions;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveDeviceTests
{
    private const int UsbBus = 7;
    private const int SataBus = 11;
    private const int SdBus = 12;
    private const int MmcBus = 13;
    private const int VirtualBus = 15;
    private const int NvmeBus = 17;

    [Theory]
    [InlineData(true, false, UsbBus, true, ConnectedDriveKind.UsbDrive)]
    [InlineData(true, false, UsbBus, false, ConnectedDriveKind.UsbDrive)]
    [InlineData(true, false, SdBus, false, ConnectedDriveKind.MemoryCard)]
    [InlineData(true, false, MmcBus, false, ConnectedDriveKind.MemoryCard)]
    [InlineData(false, true, UsbBus, true, ConnectedDriveKind.ExternalDisk)]
    [InlineData(false, true, NvmeBus, true, ConnectedDriveKind.ExternalDisk)]
    [InlineData(false, true, VirtualBus, false, ConnectedDriveKind.VirtualDisk)]
    public void TheseDrivesCount(bool removable, bool isFixed, int bus, bool ejectable, ConnectedDriveKind kind)
    {
        Assert.Equal(kind, new DriveDevice(removable, isFixed, bus, ejectable, IsWindowsDrive: false).Kind);
    }

    [Theory]
    [InlineData(false, true, SataBus, false)]
    [InlineData(false, true, NvmeBus, false)]
    [InlineData(false, false, UsbBus, true)]
    [InlineData(false, false, VirtualBus, false)]
    public void ThePCsOwnDisksAndOtherDrivesDont(bool removable, bool isFixed, int bus, bool ejectable)
    {
        Assert.Null(new DriveDevice(removable, isFixed, bus, ejectable, IsWindowsDrive: false).Kind);
    }

    [Fact]
    public void TheWindowsDriveNeverCounts()
    {
        Assert.Null(new DriveDevice(Removable: false, Fixed: true, UsbBus, Ejectable: true, IsWindowsDrive: true).Kind);
    }

    [Theory]
    [InlineData("FAT32", 4294967295L)]
    [InlineData("fat", 4294967295L)]
    [InlineData("exFAT", long.MaxValue)]
    [InlineData("NTFS", long.MaxValue)]
    public void FatDrivesHoldFilesUnder4Gb(string fileSystem, long largest)
    {
        var drive = new ConnectedDrive(@"\\?\Volume{1}\", 'E', ConnectedDriveKind.UsbDrive, false, "STICK", fileSystem, 1, null, 1, 1, false, false, false, null);

        Assert.Equal(largest, drive.MaximumFileSize);
    }
}
