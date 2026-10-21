using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

// Starting the feature creates its own hidden window and registers this process for mouse
// input; nothing moves the pointer or runs an action.
public sealed class HotCornersFeatureTests : IDisposable
{
    private readonly RecordingLogger<HotCornersFeature> _logger = new();
    private readonly HotCornersFeature _feature;

    public HotCornersFeatureTests()
    {
        _feature = new HotCornersFeature(_logger, TimeProvider.System);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheInputModule()
    {
        Assert.Equal(("hotCorners", "input"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void TurningTheFeatureOnAndOffStartsAndStopsIt()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": true, "activationDelayMilliseconds": 300 }"""));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);

        Assert.Equal(
            ["Hot Corners started.", "Hot Corners applied its settings", "Hot Corners applied its settings", "Hot Corners stopped."],
            _logger.Entries.Select(entry => entry.Message.StartsWith("Hot Corners applied its settings", StringComparison.Ordinal) ? "Hot Corners applied its settings" : entry.Message));
    }

    [Fact]
    public void UnreadableSettingsTurnTheFeatureOff()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Apply(Section("""{ "enabled": true, "topLeft": { "action": "OpenEverything" } }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The Hot Corners settings can't be read, so Hot Corners is off."), _logger.Entries);
    }

    [Fact]
    public void DisposeStopsTheFeature()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Dispose();
        _feature.Dispose();

        Assert.False(_feature.IsRunning);
        Assert.Single(_logger.Entries, entry => entry.Message == "Hot Corners stopped.");
    }

    [Fact]
    public void TheFeatureCanStartAgainAfterItStopped()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        _feature.Apply(Section("""{ "enabled": false }"""));

        _feature.Apply(Section("""{ "enabled": true }"""));

        Assert.True(_feature.IsRunning);
    }
}
