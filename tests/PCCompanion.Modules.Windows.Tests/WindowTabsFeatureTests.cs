using System;
using System.Drawing;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
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
    private const string Quiet = """{ "enabled": true, "groupByDropping": false, "switchShortcuts": "None", "groupShortcuts": "None" }""";

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

    [Theory]
    [InlineData("nextTab")]
    [InlineData("previousTab")]
    [InlineData("moveTabForward")]
    [InlineData("moveTabBack")]
    [InlineData("addToGroupBehind")]
    [InlineData("takeOutOfGroup")]
    public void ItsActionsActOnTheWindowInFrontAndRunOnlyWhileItRuns(string id)
    {
        var action = new FeatureAction("windowTabs", id);

        Assert.True(_feature.OnWindowInFront(action));
        Assert.False(_feature.CanRun(action));

        _feature.Apply(Section(Quiet));

        Assert.True(_feature.CanRun(action));
    }

    [Fact]
    public void ItOffersItsSixActionsOnTheWindowInFront()
    {
        Assert.Equal(
            ["nextTab", "previousTab", "moveTabForward", "moveTabBack", "addToGroupBehind", "takeOutOfGroup"],
            WindowTabsFeature.OfferedActions().Select(offered => offered.Action.ActionId));
        Assert.All(WindowTabsFeature.OfferedActions(), offered =>
        {
            Assert.Equal("windowTabs", offered.Action.FeatureId);
            Assert.True(offered.OnWindowInFront);
            Assert.False(string.IsNullOrWhiteSpace(offered.Name));
        });
    }

    [Fact]
    public void ItSaysWhichKeysItsShortcutsUse()
    {
        static FeatureAction Action(string id) => new("windowTabs", id);
        var windowsControl = HotKeyModifiers.Windows | HotKeyModifiers.Control;

        Assert.Equal(
            [
                (Action("nextTab"), ShortcutKeys.OfVirtualKey(windowsControl, 0x22)),
                (Action("previousTab"), ShortcutKeys.OfVirtualKey(windowsControl, 0x21)),
                (Action("moveTabForward"), ShortcutKeys.OfVirtualKey(windowsControl | HotKeyModifiers.Shift, 0x22)),
                (Action("moveTabBack"), ShortcutKeys.OfVirtualKey(windowsControl | HotKeyModifiers.Shift, 0x21)),
                (Action("addToGroupBehind"), ShortcutKeys.OfVirtualKey(windowsControl, 0x54)),
                (Action("takeOutOfGroup"), ShortcutKeys.OfVirtualKey(windowsControl | HotKeyModifiers.Shift, 0x54)),
            ],
            WindowTabsFeature.Shortcuts(new WindowTabsSettings()));
        Assert.Equal(
            [(Action("nextTab"), ShortcutKeys.OfVirtualKey(HotKeyModifiers.Windows | HotKeyModifiers.Alt, 0x22))],
            WindowTabsFeature.Shortcuts(new WindowTabsSettings { SwitchShortcuts = TabSwitchShortcuts.WindowsAltPage, GroupShortcuts = TabGroupShortcuts.None }).Take(1));
        Assert.Empty(WindowTabsFeature.Shortcuts(new WindowTabsSettings { SwitchShortcuts = (TabSwitchShortcuts)9, GroupShortcuts = (TabGroupShortcuts)7 }));
    }

    [Fact]
    public void ItRunsNoOtherAction()
    {
        _feature.Apply(Section(Quiet));

        Assert.False(_feature.CanRun(new FeatureAction("windowTabs", "closeTab")));
        Assert.False(_feature.CanRun(new FeatureAction("windowTabs", "nextTab", "Office")));
        Assert.False(_feature.CanRun(new FeatureAction("appWindows", "nextTab")));
        Assert.False(_feature.OnWindowInFront(new FeatureAction("windowTabs", "closeTab")));
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
