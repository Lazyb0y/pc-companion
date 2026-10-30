using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window on a thread of its own. The tests turn off
// both shortcuts, so nothing registers a shortcut, and nothing minimizes other apps' windows.
public sealed class HideAppsFeatureTests : IDisposable
{
    private const string Quiet = """{ "enabled": true, "hideAppShortcut": "None", "hideOthersShortcut": "None" }""";

    private readonly RecordingLogger<HideAppsFeature> _logger = new();
    private readonly HideAppsFeature _feature;

    public HideAppsFeatureTests()
    {
        _feature = new HideAppsFeature(TimeProvider.System, _logger);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("hideApps", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void TurningItOnStartsItAndTurningItOffStopsIt()
    {
        _feature.Apply(Section(Quiet));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void AnUnreadableSectionTurnsItOffWithAWarning()
    {
        _feature.Apply(Section(Quiet));

        _feature.Apply(Section("""{ "enabled": true, "frontDisplayOnly": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoWindowToBringBackTheMenuGetsNoItems()
    {
        _feature.Apply(Section(Quiet));

        Assert.Empty(_feature.GetMenuItems());
        _feature.ChooseMenuItem(1);
    }
}
