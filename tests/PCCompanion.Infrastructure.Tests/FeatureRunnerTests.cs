using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class FeatureRunnerTests
{
    private readonly RecordingLogger<FeatureRunner> _logger = new();

    [Fact]
    public void ApplyPassesEachFeatureItsOwnSection()
    {
        var first = new FakeFeature("first");
        var second = new FakeFeature("second");
        using var runner = new FeatureRunner([first, second], _logger);
        var settings = new AppSettings();
        settings.Features["first"] = Section("""{ "enabled": true }""");

        runner.Apply(settings);

        Assert.Equal(["""{ "enabled": true }"""], first.Applied);
        Assert.Equal([null], second.Applied);
    }

    [Fact]
    public void AFeatureThatFailsIsStoppedAndTheOthersKeepRunning()
    {
        var failing = new FakeFeature("failing") { Fails = true };
        var other = new FakeFeature("other");
        using var runner = new FeatureRunner([failing, other], _logger);

        runner.Apply(new AppSettings());
        runner.Apply(new AppSettings());

        Assert.Single(failing.Applied);
        Assert.Equal(1, failing.DisposeCount);
        Assert.Equal(2, other.Applied.Count);
        Assert.Equal(
            [(LogLevel.Error, "The failing feature failed, so it's stopped until PC Companion restarts.")],
            _logger.Entries);
    }

    [Fact]
    public void AFeatureThatIsNotSupportedIsNeverStarted()
    {
        var feature = new FakeFeature("unsupported") { Support = FeatureSupport.NotSupported("NeedsWindows11") };
        using var runner = new FeatureRunner([feature], _logger);

        runner.Apply(new AppSettings());
        runner.Apply(new AppSettings());

        Assert.Empty(feature.Applied);
        Assert.Equal(
            [(LogLevel.Debug, "The unsupported feature isn't supported on this PC (NeedsWindows11), so it isn't started.")],
            _logger.Entries);
    }

    [Fact]
    public void DisposeStopsEveryFeatureOnceEvenWhenOneFailsToStop()
    {
        var failing = new FakeFeature("failing") { Fails = true };
        var throwsOnStop = new FakeFeature("throwsOnStop") { FailsToStop = true };
        var other = new FakeFeature("other");
        var runner = new FeatureRunner([failing, throwsOnStop, other], _logger);
        runner.Apply(new AppSettings());

        runner.Dispose();
        runner.Dispose();

        Assert.Equal([1, 1, 1], new[] { failing, throwsOnStop, other }.Select(feature => feature.DisposeCount));
        Assert.Contains((LogLevel.Error, "The throwsOnStop feature failed to stop."), _logger.Entries);
        Assert.Throws<ObjectDisposedException>(() => runner.Apply(new AppSettings()));
    }

    private static JsonElement Section(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FakeFeature(string id) : IFeature
    {
        public FeatureInfo Info { get; } = new(id, "test", id + "Name", id + "Description", "");

        public FeatureSupport Support { get; init; } = FeatureSupport.Supported;

        public bool Fails { get; init; }

        public bool FailsToStop { get; init; }

        public List<string?> Applied { get; } = [];

        public int DisposeCount { get; private set; }

        public void Apply(JsonElement? settings)
        {
            Applied.Add(settings?.GetRawText());
            if (Fails)
            {
                throw new InvalidOperationException("The fake feature failed.");
            }
        }

        public void Dispose()
        {
            DisposeCount++;
            if (FailsToStop)
            {
                throw new InvalidOperationException("The fake feature failed to stop.");
            }
        }
    }
}
