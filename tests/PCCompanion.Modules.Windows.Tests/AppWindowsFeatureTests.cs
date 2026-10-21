using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window, watches which window is in front and
// registers its shortcut for a moment; nothing presses keys or switches windows.
public sealed class AppWindowsFeatureTests : IDisposable
{
    private readonly RecordingLogger<AppWindowsFeature> _logger = new();
    private readonly AppWindowsFeature _feature;

    public AppWindowsFeatureTests()
    {
        _feature = new AppWindowsFeature(_logger, TimeProvider.System);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("appWindows", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
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

        _feature.Apply(Section("""{ "enabled": true, "shortcut": "CtrlAboveTab" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownShortcutRegistersNoneWithAWarning()
    {
        _feature.Apply(Section("""{ "enabled": true, "shortcut": 7 }"""));

        Assert.True(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("unknown", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("registered", StringComparison.Ordinal));
    }

    private static JsonElement Section(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
