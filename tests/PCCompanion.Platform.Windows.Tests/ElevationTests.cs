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
    public void AnElevatedProcessCanElevate()
    {
        Assert.True(!Elevation.IsElevated || Elevation.CanElevate);
    }

    [Fact]
    public void TheCurrentUserHasASecurityIdentifier()
    {
        Assert.StartsWith("S-1-5-", Elevation.CurrentUserSid, StringComparison.Ordinal);
    }
}
