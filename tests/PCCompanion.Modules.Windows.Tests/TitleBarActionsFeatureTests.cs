using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window and watches the pointer for a moment; it
// claims nothing, since nothing moves the pointer onto a title bar, and clicks nothing.
public sealed class TitleBarActionsFeatureTests : IDisposable
{
    private readonly RecordingLogger<TitleBarActionsFeature> _logger = new();
    private readonly TitleBarActionsFeature _feature;

    public TitleBarActionsFeatureTests()
    {
        _feature = new TitleBarActionsFeature(_logger, TimeProvider.System, new WindowRequests());
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("titleBarActions", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.Equal(("TitleBarActionsName", "TitleBarActionsDescription"), (_feature.Info.NameKey, _feature.Info.DescriptionKey));
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
    public void TurningItOnStartsItAndTurningItOffStopsIt()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void AnUnreadableSectionTurnsItOffWithAWarning()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Apply(Section("""{ "enabled": true, "middleClick": "Close" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownActionDoesNothingWithAWarning()
    {
        _feature.Apply(Section("""{ "enabled": true, "shiftMiddleClick": 9 }"""));

        Assert.True(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("unknown", StringComparison.Ordinal));
    }
}
