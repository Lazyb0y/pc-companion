using System;
using System.Drawing;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Modules.Windows.WindowTabs;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden windows and hooks on a thread of its own. The tests
// turn off grouping by dropping and the shortcuts, so no window becomes a tab and nothing is
// registered, and windows left marked are looked for among made-up ones.
public sealed class WindowTabsFeatureTests : IDisposable
{
    private const string Quiet = """{ "enabled": true, "groupByDropping": false, "switchShortcuts": "None" }""";

    private readonly RecordingLogger<WindowTabsFeature> _logger = new();
    private readonly FakeTabsSystem _left = new(action => action());
    private readonly WindowTabsFeature _feature;
    private int _recoveries;

    public WindowTabsFeatureTests()
    {
        _feature = new WindowTabsFeature(new FakeHostCommands(), TimeProvider.System, _logger, new WindowTabsWindows(), new DropDownWindows(), new ShortcutRefusals(), () =>
        {
            _recoveries++;
            return _left;
        });
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("windowTabs", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOffButBringsBackWindowsLeftHidden()
    {
        var left = _left.Add(1, new Rectangle(100, 100, 800, 600));
        left.Shown = false;
        _left.Mark(1, owned: false);

        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.True(left.Shown && left.Minimized);
        Assert.Empty(_left.Marks);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("brought back 1", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsLeftHiddenAreLookedForOnlyOnce()
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

        _feature.Apply(Section("""{ "enabled": true, "groupByDropping": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }
}
