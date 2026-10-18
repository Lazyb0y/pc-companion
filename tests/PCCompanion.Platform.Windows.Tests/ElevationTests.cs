using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ElevationTests
{
    [Fact]
    public void IsElevatedMatchesTheProcess()
    {
        Assert.Equal(Environment.IsPrivilegedProcess, Elevation.IsElevated);
    }

    [Fact]
    public void AProcessElevatedThroughUacIsElevated()
    {
        Assert.True(!Elevation.IsUacElevated || Elevation.IsElevated);
    }

    [Fact]
    public void AProcessLimitedByUacIsNotElevatedAndCanElevate()
    {
        Assert.True(!Elevation.IsUacLimited || (!Elevation.IsElevated && Elevation.CanElevate && !Elevation.IsUacElevated));
    }

    [Fact]
    public void AnElevatedProcessCanElevate()
    {
        Assert.True(!Elevation.IsElevated || Elevation.CanElevate);
    }

    [Fact]
    public void TheElevationOfAProcessIsReadFromItsToken()
    {
        Assert.Equal(Elevation.IsElevated, Elevation.IsProcessElevated((uint)Environment.ProcessId));
    }

    // Windows lets no program open the System Idle Process, whose ID is 0.
    [Fact]
    public void AProcessThatCantBeOpenedHasNoKnownElevation()
    {
        Assert.Null(Elevation.IsProcessElevated(0));
    }

    [Theory]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1001", true)]
    [InlineData("S-1-5-32-544", false)]
    [InlineData("S-1-5-18", false)]
    [InlineData("not a sid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyUserAccountsAreAccounts(string? sid, bool expected)
    {
        Assert.Equal(expected, Elevation.IsAccountSid(sid));
    }

    [Fact]
    public void TheCurrentUserIsAnAccount()
    {
        Assert.True(Elevation.IsAccountSid(Elevation.CurrentUserSid));
    }

    [Fact]
    public void TheCurrentUserHasASecurityIdentifier()
    {
        Assert.StartsWith("S-1-5-", Elevation.CurrentUserSid, StringComparison.Ordinal);
    }
}
