using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window on a thread of its own. The tests list no
// apps, so nothing registers a shortcut or moves other apps' windows, and windows left marked are
// looked for among made-up ones.
public sealed class DropDownAppsFeatureTests : IDisposable
{
    private const string Quiet = """{ "enabled": true }""";

    private readonly RecordingLogger<DropDownAppsFeature> _logger = new();
    private readonly FakeDropDownSystem _left = new(new ManualTimeProvider());
    private readonly DropDownAppsFeature _feature;
    private int _recoveries;

    public DropDownAppsFeatureTests()
    {
        _feature = new DropDownAppsFeature(new FakeHostCommands(), TimeProvider.System, _logger, new DropDownWindows(), new ShortcutRefusals(), () =>
        {
            _recoveries++;
            return _left;
        });
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("dropDownApps", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOffButBringsBackWindowsLeftHidden()
    {
        var left = _left.Add(1);
        (left.Visible, left.Marked) = (false, true);

        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.True(left.Visible && left.Minimized);
        Assert.False(left.Marked);
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

        _feature.Apply(Section("""{ "enabled": true, "width": "wide" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void ItOffersShowingOrHidingEachListedAppByItsAppIdOrProgram()
    {
        var settings = new DropDownAppsSettings
        {
            Apps =
            [
                new DropDownApp { Name = "Terminal", AppId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" },
                new DropDownApp { Name = "Notes", Program = @"C:\Apps\Notes.exe" },
                new DropDownApp { Name = "Nothing" },
            ],
        };

        var offered = DropDownAppsFeature.OfferedActions(settings);

        Assert.Equal(
            [new FeatureAction("dropDownApps", "showOrHide", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"), new FeatureAction("dropDownApps", "showOrHide", @"C:\Apps\Notes.exe")],
            offered.Select(action => action.Action));
        Assert.Contains("Terminal", offered[0].Name, StringComparison.Ordinal);
        Assert.Contains("Notes", offered[1].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ItCanShowOrHideOnlyAnAppItLists()
    {
        var notes = new FeatureAction("dropDownApps", "showOrHide", @"C:\Apps\Notes.exe");
        Assert.False(_feature.CanRun(notes));

        _feature.Apply(Section("""{ "enabled": true, "apps": [ { "name": "Notes", "program": "C:\\Apps\\Notes.exe", "shortcut": "None" } ] }"""));

        Assert.True(_feature.CanRun(notes));
        Assert.True(_feature.CanRun(notes with { Target = @"c:\apps\notes.EXE" }));
        Assert.False(_feature.CanRun(notes with { Target = @"C:\Apps\Other.exe" }));
        Assert.False(_feature.CanRun(notes with { Target = null }));
    }
}
