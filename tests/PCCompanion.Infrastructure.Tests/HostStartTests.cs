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
}
