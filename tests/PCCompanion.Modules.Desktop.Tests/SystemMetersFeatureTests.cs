using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Desktop.Tests;

// Starting the feature runs the meters thread with its own hidden window, and fake icons.
[Collection(nameof(MetersThreads))]
public sealed class SystemMetersFeatureTests : IDisposable
{
    private readonly FakeMetersSystem _system = new();
    private readonly RecordingLogger<SystemMetersFeature> _logger = new();
    private readonly SystemMetersFeature _feature;

    public SystemMetersFeatureTests()
    {
        _feature = new SystemMetersFeature(_system, new FakeHostCommands(), TimeProvider.System, _logger);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheDesktopModule()
    {
        Assert.Equal(("systemMeters", "desktop"), (_feature.Info.Id, _feature.Info.ModuleId));
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
    public void TurningTheFeatureOnShowsTheMetersAndOffRemovesThem()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_feature.IsRunning);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Cpu")));

        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.False(_feature.IsRunning);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("dispose")));
    }

    [Fact]
    public void WithEveryMeterOffNothingRuns()
    {
        _feature.Apply(Section("""{ "enabled": true, "cpu": false, "memory": false, "network": false }"""));

        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void TurningTheFeatureOffAndOnAgainStartsANewThreadAfterTheOldOne()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        _feature.Apply(Section("""{ "enabled": false }"""));
        _feature.Apply(Section("""{ "enabled": true }"""));

        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Cpu")));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public void UnreadableSettingsTurnTheFeatureOff()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Apply(Section("""{ "enabled": true, "cpu": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The System Meters settings can't be read, so System Meters is off."), _logger.Entries);
    }

    [Fact]
    public void AnUnknownChoiceIsLoggedAndTheFeatureRuns()
    {
        _feature.Apply(Section("""{ "enabled": true, "layout": "Spiral" }"""));

        Assert.True(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The System Meters setting layout has a choice this version doesn't know, so it uses the default."), _logger.Entries);
    }

    [Fact]
    public void AFailureKeepsTheFeatureOffUntilTheHostRestarts()
    {
        _system.ShowFailure = new InvalidOperationException("The test fails the meters.");
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Level == LogLevel.Error)));

        _system.ShowFailure = null;
        _feature.Apply(Section("""{ "enabled": true, "updateIntervalSeconds": 2 }"""));

        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void ASecondDisposeDoesntWaitAgain()
    {
        using var gate = new ManualResetEventSlim();
        _system.DisposeGate = gate;
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Cpu")));

        // As the host does at exit: its feature runner, then its services.
        _feature.Dispose();
        _feature.Dispose();

        Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        gate.Set();
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Contains((LogLevel.Debug, "System Meters' thread ended."))));
    }

    [Fact]
    public void DisposeWaitsForTheMetersToBeRemoved()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Cpu")));

        _feature.Dispose();

        Assert.Equal("dispose", _system.Events[^1]);
    }
}
