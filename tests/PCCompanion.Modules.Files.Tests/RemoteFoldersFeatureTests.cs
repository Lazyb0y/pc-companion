using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Files.Tests;

public sealed class RemoteFoldersFeatureTests
{
    private readonly RecordingLogger<RemoteFoldersFeature> _logger = new();

    [Fact]
    public void WithoutASectionTheFeatureIsOff()
    {
        using var feature = new RemoteFoldersFeature(_logger);

        feature.Apply(null);

        Assert.False(feature.IsRunning);
    }

    [Fact]
    public void TurningItOnAndOffStartsAndStopsIt()
    {
        using var feature = new RemoteFoldersFeature(_logger);

        feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(feature.IsRunning);

        feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(feature.IsRunning);
    }

    [Fact]
    public void SettingsThatCantBeReadTurnItOffWithAWarning()
    {
        using var feature = new RemoteFoldersFeature(_logger);
        feature.Apply(Section("""{ "enabled": true }"""));

        feature.Apply(Section("""{ "enabled": true, "servers": 3 }"""));

        Assert.False(feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ItsDefinitionIsInTheFilesModule()
    {
        Assert.Equal("remoteFolders", RemoteFoldersFeature.Definition.Id);
        Assert.Equal(FilesModule.Id, RemoteFoldersFeature.Definition.ModuleId);
        Assert.True(RemoteFoldersFeature.CurrentSupport.IsSupported);
    }
}
