using System.Collections.Generic;
using PCCompanion.Core.Settings;
using PCCompanion.Modules.Files.RemoteFolders;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.RemoteFolders.Tests;

public sealed class ProviderStartTests
{
    [Fact]
    public void TheHostsProcessIdFollowsItsArgument()
    {
        Assert.Equal(4242, ProviderArguments.Parse(["--host", "4242"]).HostProcessId);
        Assert.Equal(7, ProviderArguments.Parse(["--other", "--HOST", "7"]).HostProcessId);
    }

    [Theory]
    [InlineData]
    [InlineData("--host")]
    [InlineData("--host", "abc")]
    [InlineData("--host", "-5")]
    [InlineData("--host", "0")]
    [InlineData("4242")]
    public void WithoutAValidHostTheProcessIdIsMissing(params string[] args)
    {
        Assert.Null(ProviderArguments.Parse(args).HostProcessId);
    }

    [Fact]
    public void WithoutASectionTheFeatureIsOff()
    {
        var read = ProviderSettings.Read(new AppSettings(), out var error);

        Assert.Null(error);
        Assert.NotNull(read);
        Assert.False(read.Enabled);
    }

    [Fact]
    public void TheFeaturesSectionIsRead()
    {
        var settings = new AppSettings { Features = new() { [RemoteFoldersFeature.Definition.Id] = Section("""{ "enabled": true, "servers": [{ "name": "Home" }] }""") } };

        var read = ProviderSettings.Read(settings, out var error);

        Assert.Null(error);
        Assert.True(read?.Enabled);
        Assert.Equal("Home", Assert.Single(read!.Servers).Name);
    }

    [Fact]
    public void ASectionThatCantBeReadGivesTheError()
    {
        var settings = new AppSettings { Features = new() { [RemoteFoldersFeature.Definition.Id] = Section("""{ "servers": 3 }""") } };

        Assert.Null(ProviderSettings.Read(settings, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ACredentialsTargetNamesItsServer()
    {
        Assert.Equal("PCCompanion/RemoteFolders/7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e", RemoteFoldersProcess.CredentialTarget("7d2c4b1e9f0a4c3b8e5d6a7f1b2c3d4e"));
        Assert.EndsWith("PCCompanion.RemoteFolders.exe", RemoteFoldersProcess.ExecutablePath, System.StringComparison.Ordinal);
    }
}
