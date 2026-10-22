using System;
using PCCompanion.Core;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class SettingsAppProcessTests
{
    [Fact]
    public void AFeaturesPageIsAskedForWithItsId()
    {
        Assert.Equal(["--feature", "networkProfiles"], SettingsAppProcess.FeatureArguments("networkProfiles"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("network profiles")]
    [InlineData("\"networkProfiles\"")]
    [InlineData("../other")]
    public void OnlyFeatureIdsCanBeAskedFor(string featureId)
    {
        Assert.Throws<ArgumentException>(() => SettingsAppProcess.FeatureArguments(featureId));
    }

    [Fact]
    public void TheFeatureIsReadFromTheArgumentsWhereverItIs()
    {
        Assert.Equal("networkProfiles", SettingsAppProcess.FeatureFromArguments(["--feature", "networkProfiles"]));
        Assert.Equal("hotCorners", SettingsAppProcess.FeatureFromArguments([@"C:\Program Files\PC Companion\PCCompanion.exe", "--FEATURE", "hotCorners"]));
    }

    [Fact]
    public void ArgumentsWithoutAFeatureIdAskForNone()
    {
        Assert.Null(SettingsAppProcess.FeatureFromArguments([]));
        Assert.Null(SettingsAppProcess.FeatureFromArguments(["--feature"]));
        Assert.Null(SettingsAppProcess.FeatureFromArguments(["--feature", ""]));
        Assert.Null(SettingsAppProcess.FeatureFromArguments(["--feature", "network profiles"]));
        Assert.Null(SettingsAppProcess.FeatureFromArguments(["--other", "networkProfiles"]));
    }
}
