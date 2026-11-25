using System;
using System.Drawing;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Windows.WindowShading;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden window on a thread of its own. Hooks are set only
// while a window is rolled up, and the tests roll up none, so no other app's window is touched.
public sealed class WindowShadingFeatureTests : IDisposable
{
    private const string On = """{ "enabled": true }""";

    private readonly RecordingLogger<WindowShadingFeature> _logger = new();
    private readonly WindowRequests _requests = new();
    private readonly FakeShadingSystem _left = new();
    private readonly WindowShadingFeature _feature;
    private int _recoveries;

    public WindowShadingFeatureTests()
    {
        _feature = new WindowShadingFeature(TimeProvider.System, _logger, new ShadedWindows(), new DropDownWindows(), new WindowTabsWindows(), _requests, () =>
        {
            _recoveries++;
            return _left;
        });
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("windowShading", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void WindowsLeftRolledUpAreRolledDownOnceOnOrOff()
    {
        _left.Add(1, new Rectangle(100, 100, 800, 600)).Minimized = true;
        _left.Add(2, new Rectangle(200, 200, 800, 600));
        _left.Marks.UnionWith([1, 2]);

        _feature.Apply(null);
        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.False(_feature.IsRunning);
        Assert.False(_left[1].Minimized);
        Assert.Equal(["roll down 1 activate False"], _left.Calls);
        Assert.Empty(_left.Marks);
        Assert.Equal(1, _recoveries);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("rolled down 1 windows left rolled up", StringComparison.Ordinal));
    }

    [Fact]
    public void TurningItOnStartsItAndTurningItOffStopsIt()
    {
        _feature.Apply(Section(On));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
    }

    [Fact]
    public void WhileItsOffItRollsUpNoWindowFromATitleBar()
    {
        Assert.False(_requests.RollUp(1));

        _feature.Apply(Section(On));
        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.False(_requests.RollUp(1));
    }

    [Fact]
    public void AnUnreadableSectionTurnsItOffWithAWarning()
    {
        _feature.Apply(Section(On));

        _feature.Apply(Section("""{ "enabled": true, "keepOnTop": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoWindowRolledUpTheMenuGetsNoItems()
    {
        _feature.Apply(Section(On));

        Assert.Empty(_feature.GetMenuItems());
        _feature.ChooseMenuItem(1);
    }

    [Fact]
    public void ItOffersRollUpRollDownLastAndRollDownAll()
    {
        Assert.Equal(
            [new FeatureAction("windowShading", "rollUp"), new FeatureAction("windowShading", "rollDownLast"), new FeatureAction("windowShading", "rollDownAll")],
            WindowShadingFeature.OfferedActions().Select(offered => offered.Action));
        Assert.All(WindowShadingFeature.OfferedActions(), offered => Assert.NotEmpty(offered.Name));
    }

    [Fact]
    public void OnlyRollUpActsOnTheWindowInFrontAsTheFeatureSays()
    {
        Assert.Equal([true, false, false], WindowShadingFeature.OfferedActions().Select(offered => offered.OnWindowInFront));
        Assert.All(WindowShadingFeature.OfferedActions(), offered => Assert.Equal(offered.OnWindowInFront, _feature.OnWindowInFront(offered.Action)));
    }

    [Fact]
    public void ItCanRunItsActionsOnlyWhileItsOn()
    {
        var rollDownAll = new FeatureAction("windowShading", "rollDownAll");
        Assert.False(_feature.CanRun(rollDownAll));

        _feature.Apply(Section(On));
        Assert.True(_feature.CanRun(rollDownAll));
        Assert.False(_feature.CanRun(rollDownAll with { Target = "Notes" }));
        Assert.False(_feature.CanRun(new FeatureAction("windowShading", "rollSideways")));
        Assert.False(_feature.CanRun(new FeatureAction("hideApps", "rollDownAll")));

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.CanRun(rollDownAll));
    }
}
