using PCCompanion.Core;
using PCCompanion.Core.Settings;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class HostStartTests
{
    [Theory]
    [InlineData(false, false, HostStart.Run)]
    [InlineData(true, false, HostStart.Exit)]
    [InlineData(false, true, HostStart.Run)]
    [InlineData(true, true, HostStart.Run)]
    public void WithoutRunAsAdministratorOnlyStartWithWindowsDecides(bool startedAtSignIn, bool startWithWindows, HostStart expected)
    {
        var general = new GeneralSettings { StartWithWindows = startWithWindows };

        Assert.Equal(expected, HostProcess.DecideStart(general, startedAtSignIn, elevated: false, hostRunning: false));
    }

    [Fact]
    public void AtSignInTheCopyWithoutAdminRightsLeavesTheWorkToTheSignInTask()
    {
        var general = new GeneralSettings { StartWithWindows = true, AlwaysRunAsAdministrator = true };

        Assert.Equal(HostStart.Exit, HostProcess.DecideStart(general, startedAtSignIn: true, elevated: false, hostRunning: false));
        Assert.Equal(HostStart.Run, HostProcess.DecideStart(general, startedAtSignIn: true, elevated: true, hostRunning: false));
    }

    [Fact]
    public void AtSignInWithStartWithWindowsOffBothCopiesExit()
    {
        var general = new GeneralSettings { StartWithWindows = false, AlwaysRunAsAdministrator = true };

        Assert.Equal(HostStart.Exit, HostProcess.DecideStart(general, startedAtSignIn: true, elevated: false, hostRunning: false));
        Assert.Equal(HostStart.Exit, HostProcess.DecideStart(general, startedAtSignIn: true, elevated: true, hostRunning: false));
    }

    [Fact]
    public void OtherStartsWithoutAdminRightsRunTheOnRequestTask()
    {
        var general = new GeneralSettings { AlwaysRunAsAdministrator = true };

        Assert.Equal(HostStart.RunTask, HostProcess.DecideStart(general, startedAtSignIn: false, elevated: false, hostRunning: false));
        Assert.Equal(HostStart.Run, HostProcess.DecideStart(general, startedAtSignIn: false, elevated: true, hostRunning: false));
    }

    // The running host gets the second start's request to open the settings.
    [Fact]
    public void AStartWhileAHostRunsDoesNotRunTheTask()
    {
        var general = new GeneralSettings { AlwaysRunAsAdministrator = true };

        Assert.Equal(HostStart.Run, HostProcess.DecideStart(general, startedAtSignIn: false, elevated: false, hostRunning: true));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)] // Always run as administrator is off.
    [InlineData(true, false, false, false)] // UAC is off, or the settings app runs as administrator.
    [InlineData(true, true, true, false)] // The host runs as administrator.
    [InlineData(true, true, null, false)] // No host runs, or its rights can't be read.
    public void AHostWithoutAdminRightsIsReportedOnlyWhenItsRightsAreKnown(bool runAsAdministrator, bool uacLimited, bool? hostElevated, bool expected)
    {
        var general = new GeneralSettings { AlwaysRunAsAdministrator = runAsAdministrator };

        Assert.Equal(expected, HostProcess.RunsWithoutAdminRights(general, uacLimited, hostElevated));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)] // With UAC off, the host runs as administrator without the setting.
    [InlineData(true, false, false)] // The host's hand-off to the scheduled task failed.
    [InlineData(true, null, true)] // No host runs, or its rights can't be read.
    [InlineData(false, null, false)]
    public void WhetherTheHostRunsAsAdministratorComesFromItsProcessBeforeTheSetting(bool runAsAdministrator, bool? hostElevated, bool expected)
    {
        var general = new GeneralSettings { AlwaysRunAsAdministrator = runAsAdministrator };

        Assert.Equal(expected, HostProcess.RunsAsAdministrator(general, hostElevated));
    }
}
