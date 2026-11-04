using System.Linq;
using PCCompanion.Modules.Files.DriveActions;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveIdentityTests
{
    [Fact]
    public void ANewIdIsRandomAndWellFormed()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => DriveIdentity.NewId()).ToArray();

        Assert.All(ids, id => Assert.True(DriveIdentity.IsId(id)));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void ASerialsHashIsTheSameEachTimeAndHidesTheSerial()
    {
        var hash = DriveIdentity.HashVolumeSerial(0xA8D4F2F8);

        Assert.True(DriveIdentity.IsHash(hash));
        Assert.Equal(hash, DriveIdentity.HashVolumeSerial(0xA8D4F2F8));
        Assert.NotEqual(hash, DriveIdentity.HashVolumeSerial(0xA8D4F2F9));
        Assert.DoesNotContain("a8d4f2f8", hash, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AHardwareSerialsHashIgnoresTheSpacesAroundIt()
    {
        Assert.Equal(DriveIdentity.HashHardwareSerial("0LVF0HQ5"), DriveIdentity.HashHardwareSerial("  0LVF0HQ5 "));
        Assert.True(DriveIdentity.IsHash(DriveIdentity.HashHardwareSerial("0LVF0HQ5")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ADeviceWithoutAHardwareSerialHasNoHash(string? serial)
    {
        Assert.Equal(string.Empty, DriveIdentity.HashHardwareSerial(serial));
    }

    [Theory]
    [InlineData("5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f", "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f")]
    [InlineData("  5F0C9A0E3B1D4C7E9A2F6B8D1C3E5A7F\r\n", "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f")]
    [InlineData("5f0c9a0e3b1d4c7e", null)]
    [InlineData("not an id", null)]
    [InlineData("", null)]
    public void AnIdFilesTextGivesItsId(string text, string? id)
    {
        Assert.Equal(id, DriveIdentity.ReadId(text));
    }
}
