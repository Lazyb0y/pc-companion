using System;
using PCCompanion.Infrastructure.Updates;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("v1.20.300", 1, 20, 300)]
    [InlineData("v999999999.0.0", 999999999, 0, 0)]
    public void TagsOfReleasesAreRead(string tag, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParseTag(tag, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("0.2.0")]
    [InlineData("V0.2.0")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0.1")]
    [InlineData("v0.2.0-beta.1")]
    [InlineData("v0.2.0+abc")]
    [InlineData("v01.2.0")]
    [InlineData("v0.02.0")]
    [InlineData("v1000000000.0.0")]
    [InlineData(" v0.2.0")]
    [InlineData("v0.2.0 ")]
    [InlineData("v0..2")]
    [InlineData("v0.2.")]
    [InlineData("v-1.0.0")]
    [InlineData("v+1.0.0")]
    [InlineData("v٠.٢.٠")]
    [InlineData("v０.２.０")]
    public void OtherTagsAreRefused(string? tag)
    {
        Assert.False(ReleaseVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void SavedVersionsAreReadWithoutTheV()
    {
        Assert.True(ReleaseVersion.TryParse("0.2.0", out var version));
        Assert.Equal(new ReleaseVersion(0, 2, 0), version);
        Assert.False(ReleaseVersion.TryParse("v0.2.0", out _));
    }

    [Fact]
    public void ToStringWritesTheThreeParts()
    {
        Assert.Equal("1.20.300", new ReleaseVersion(1, 20, 300).ToString());
    }

    [Fact]
    public void TheRunningVersionIsTheFirstThreePartsOfTheAssemblyVersion()
    {
        Assert.Equal(new ReleaseVersion(0, 1, 0), ReleaseVersion.FromAssemblyVersion(new Version(0, 1, 0, 0)));
        Assert.Equal(new ReleaseVersion(1, 2, 0), ReleaseVersion.FromAssemblyVersion(new Version(1, 2)));
    }

    [Fact]
    public void VersionsCompareByMajorThenMinorThenPatch()
    {
        Assert.True(new ReleaseVersion(0, 10, 0) > new ReleaseVersion(0, 9, 9));
        Assert.True(new ReleaseVersion(1, 0, 0) > new ReleaseVersion(0, 99, 99));
        Assert.True(new ReleaseVersion(0, 1, 1) > new ReleaseVersion(0, 1, 0));
        Assert.True(new ReleaseVersion(0, 1, 0) <= new ReleaseVersion(0, 1, 0));
        Assert.True(new ReleaseVersion(0, 1, 0) >= new ReleaseVersion(0, 1, 0));
        Assert.True(new ReleaseVersion(0, 1, 0) < new ReleaseVersion(0, 2, 0));
        Assert.Equal(0, new ReleaseVersion(0, 1, 0).CompareTo(new ReleaseVersion(0, 1, 0)));
    }

    [Fact]
    public void AnUpdateIsOnlyANewerVersion()
    {
        var current = new ReleaseVersion(0, 2, 0);

        Assert.Equal(new ReleaseVersion(0, 3, 0), new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 3, 0) }.NewerThan(current));
        Assert.Null(new UpdateCheckState { LatestVersion = current }.NewerThan(current));
        Assert.Null(new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 1, 9) }.NewerThan(current));
        Assert.Null(new UpdateCheckState().NewerThan(current));
    }
}
