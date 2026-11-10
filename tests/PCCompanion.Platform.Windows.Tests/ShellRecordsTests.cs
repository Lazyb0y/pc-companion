using System;
using Microsoft.Win32;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test reads a key of its own, which stands for the user's.
public sealed class ShellRecordsTests
{
    [Theory]
    [InlineData(19045, false)]
    [InlineData(22000, false)]
    [InlineData(22621, true)]
    [InlineData(26100, true)]
    [InlineData(ShellRecords.MaxTestedBuild, true)]
    [InlineData(ShellRecords.MaxTestedBuild + 1, false)]
    public void TheRecordsAreReadOnlyOnTestedBuilds(int build, bool tested) =>
        Assert.Equal(tested, ShellRecords.IsTestedBuild(build));

    [Fact]
    public void TheVirtualDesktopInUseIsAnId()
    {
        using var user = new TestRegistryKey();
        var id = Guid.NewGuid();
        using (var key = user.Key.CreateSubKey(ShellRecords.VirtualDesktopsKey))
        {
            key.SetValue("CurrentVirtualDesktop", id.ToByteArray(), RegistryValueKind.Binary);
        }

        Assert.Equal(id, ShellRecords.CurrentVirtualDesktop(user.Key));
    }

    [Fact]
    public void WithOneDesktopOrAnotherRecordItsUnknown()
    {
        using var user = new TestRegistryKey();
        Assert.Null(ShellRecords.CurrentVirtualDesktop(user.Key));

        using (var key = user.Key.CreateSubKey(ShellRecords.VirtualDesktopsKey))
        {
            key.SetValue("CurrentVirtualDesktop", new byte[8], RegistryValueKind.Binary);
        }

        Assert.Null(ShellRecords.CurrentVirtualDesktop(user.Key));
    }

    [Fact]
    public void SpotlightIsOnWhenItsStateSaysSo()
    {
        using var user = new TestRegistryKey();
        Assert.False(ShellRecords.IsSpotlightOn(user.Key));

        using var key = user.Key.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings");
        key.SetValue("EnabledState", 0, RegistryValueKind.DWord);
        Assert.False(ShellRecords.IsSpotlightOn(user.Key));

        key.SetValue("EnabledState", 1, RegistryValueKind.DWord);
        Assert.True(ShellRecords.IsSpotlightOn(user.Key));
    }
}
