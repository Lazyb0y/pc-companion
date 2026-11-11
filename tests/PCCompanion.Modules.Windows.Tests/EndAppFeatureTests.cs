using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Windows.EndApp;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

// Starting the feature creates its own hidden windows on a thread of its own. The tests turn off
// the shortcut, so nothing registers one, and never choose the menu item, which would cover the
// screen; the Sandbox checks do.
public sealed class EndAppFeatureTests : IDisposable
{
    private const string Quiet = """{ "enabled": true, "shortcut": "None" }""";

    private readonly RecordingLogger<EndAppFeature> _logger = new();
    private readonly EndAppFeature _feature;

    public EndAppFeatureTests() => _feature = new EndAppFeature(TimeProvider.System, _logger, new NoNotifications(), new ShortcutRefusals());

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheWindowsModule()
    {
        Assert.Equal(("endApp", "windows"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.Equal(("EndAppName", "EndAppDescription"), (_feature.Info.NameKey, _feature.Info.DescriptionKey));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOffWithoutAMenuItem()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Empty(_feature.GetMenuItems());
    }

    [Fact]
    public void TurningItOnAddsTheMenuItemAndTurningItOffRemovesIt()
    {
        _feature.Apply(Section(Quiet));
        Assert.True(_feature.IsRunning);
        var item = Assert.Single(_feature.GetMenuItems());
        Assert.Equal(Strings.EndAppMenuItem, item.Text);

        _feature.Apply(Section(Quiet));
        Assert.True(_feature.IsRunning);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
        Assert.Empty(_feature.GetMenuItems());
    }

    [Fact]
    public void AnUnreadableSectionTurnsItOffWithAWarning()
    {
        _feature.Apply(Section(Quiet));

        _feature.Apply(Section("""{ "enabled": true, "shortcut": [] }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingTwiceIsFine()
    {
        _feature.Apply(Section(Quiet));

        _feature.Dispose();
        _feature.Dispose();

        Assert.False(_feature.IsRunning);
    }

    private sealed class NoNotifications : IHostNotifications
    {
        public bool Show(string title, string text, Action? selected) => true;
    }

    [Fact]
    public void ItOffersChoosingAnAppToEndWhileItsOn()
    {
        var chooseApp = Assert.Single(EndAppFeature.OfferedActions()).Action;
        Assert.Equal(new FeatureAction("endApp", "chooseApp"), chooseApp);
        Assert.False(_feature.CanRun(chooseApp));

        _feature.Apply(Section(Quiet));

        Assert.True(_feature.CanRun(chooseApp));
        Assert.False(_feature.CanRun(chooseApp with { Target = "Notepad" }));
    }
}
