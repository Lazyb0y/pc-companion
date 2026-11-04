using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the drive Windows is on, without opening its files; no drive is connected, ejected or changed.
public sealed class DriveDevicesTests
{
    private static readonly char WindowsDrive = Path.GetPathRoot(Environment.SystemDirectory)![0];

    [Fact]
    public void TheWindowsDriveIsReadWithoutAdminRights()
    {
        var drive = DriveDevices.Read(WindowsDrive);

        Assert.NotNull(drive);
        Assert.True(drive.IsWindowsDrive);
        Assert.True(drive.Fixed);
        Assert.False(drive.Removable);
        Assert.False(drive.IsLocked);
        Assert.StartsWith(@"\\?\Volume{", drive.Volume, StringComparison.Ordinal);
        Assert.NotEmpty(drive.FileSystem);
        Assert.InRange(drive.Free, 1, drive.Size);
        Assert.NotEqual(0, drive.BusType);
    }

    [Fact]
    public void TheLettersInUseIncludeTheWindowsDrive()
    {
        Assert.NotEqual(0u, DriveDevices.Letters & (1u << (char.ToUpperInvariant(WindowsDrive) - 'A')));
    }

    [Fact]
    public void ALetterWithoutADriveHasNothingToRead()
    {
        var free = Enumerable.Range(0, 26).Where(index => (DriveDevices.Letters & (1u << index)) == 0).Select(index => (char)('A' + index)).Last();

        Assert.Null(DriveDevices.Read(free));
    }

    [Theory]
    [InlineData('1')]
    [InlineData('\\')]
    public void OnlyLettersNameDrives(char letter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DriveDevices.Read(letter));
    }

    [Fact]
    public void TheWindowsDriveIsNeverEjected()
    {
        var result = DriveDevices.Eject(WindowsDrive);

        Assert.False(result.Ejected);
        Assert.True(result.Vetoed);
    }
}
