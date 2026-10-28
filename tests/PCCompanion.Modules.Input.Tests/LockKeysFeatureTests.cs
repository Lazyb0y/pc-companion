using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Input.LockKeys;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

// Starting the feature creates its own hidden windows and only reads the lock keys. No test
// changes how the notice shows while the feature runs, which would show it.
public sealed class LockKeysFeatureTests : IDisposable
{
    private readonly RecordingLogger<LockKeysFeature> _logger = new();
    private readonly LockKeysFeature _feature;

    public LockKeysFeatureTests()
    {
        _feature = new LockKeysFeature(_logger, TimeProvider.System);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheInputModule()
    {
        Assert.Equal(("lockKeys", "input"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.Equal(("LockKeysName", "LockKeysDescription"), (_feature.Info.NameKey, _feature.Info.DescriptionKey));
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

        _feature.Apply(Section("""{ "enabled": true, "scrollLock": true }"""));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);

        Assert.Equal(
            ["Lock Keys started.", "Lock Keys applied its settings", "Lock Keys applied its settings", "Lock Keys stopped."],
            _logger.Entries.Select(entry => entry.Message.StartsWith("Lock Keys applied its settings", StringComparison.Ordinal) ? "Lock Keys applied its settings" : entry.Message));
    }

    [Fact]
    public void TheSameSettingsAgainChangeNothing()
    {
        // As when another feature's settings are saved.
        _feature.Apply(Section("""{ "enabled": true }"""));
        _feature.Apply(Section("""{ "enabled": true }"""));

        Assert.Single(_logger.Entries, entry => entry.Message.StartsWith("Lock Keys applied its settings", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableSettingsTurnTheFeatureOff()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Apply(Section("""{ "enabled": true, "position": "Middle" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The Lock Keys settings can't be read, so Lock Keys is off."), _logger.Entries);
    }

    [Fact]
    public void DisposeStopsTheFeature()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Dispose();
        _feature.Dispose();

        Assert.False(_feature.IsRunning);
        Assert.Single(_logger.Entries, entry => entry.Message == "Lock Keys stopped.");
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
