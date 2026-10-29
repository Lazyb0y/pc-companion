using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window on a thread of its own. The tests turn off the
// shortcut and Shift + Minimize, so nothing registers a shortcut or watches other apps' windows,
// and windows left marked are looked for among made-up ones.
public sealed class MinimizeToTrayFeatureTests : IDisposable
{
    private const string Quiet = """{ "enabled": true, "shortcut": "None", "shiftMinimize": false }""";

    private readonly RecordingLogger<MinimizeToTrayFeature> _logger = new();
    private readonly FakeTrayWindowsSystem _left = new();
    private readonly MinimizeToTrayFeature _feature;
    private int _recoveries;

    public MinimizeToTrayFeatureTests()
    {
        _feature = new MinimizeToTrayFeature(new FakeHostCommands(), TimeProvider.System, _logger, () =>
        {
            _recoveries++;
            return _left;
        });
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("minimizeToTray", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOffButBringsBackWindowsLeftInTheTray()
    {
        var left = _left.Add(1);
        (left.Visible, left.Minimized, left.Marked) = (false, true, true);

        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.True(left.Visible);
        Assert.False(left.Marked);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("brought back 1", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsLeftInTheTrayAreLookedForOnlyOnce()
    {
        _feature.Apply(null);
        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.Equal(1, _recoveries);
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

        _feature.Apply(Section("""{ "enabled": true, "shiftMinimize": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoWindowInTheTrayTheMenuGetsNoItems()
    {
        _feature.Apply(Section(Quiet));

        Assert.Empty(_feature.GetMenuItems());
        _feature.ChooseMenuItem(1);
    }
}
