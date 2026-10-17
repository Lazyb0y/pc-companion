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
    public void AnElevatedProcessCanElevate()
    {
        Assert.True(!Elevation.IsElevated || Elevation.CanElevate);
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
