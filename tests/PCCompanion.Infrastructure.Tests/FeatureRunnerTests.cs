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
using static PCCompanion.Testing.TestJson;

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

    [Fact]
    public void MenuItemsGetMenuIdsInTheFeaturesOrder()
    {
        var first = new FakeMenuFeature("first", new HostMenuItem(7, "Seven"), HostMenuItem.Separator, new HostMenuItem(0, "A note"));
        var plain = new FakeFeature("plain");
        var second = new FakeMenuFeature("second", HostMenuItem.Submenu("Profiles", [new HostMenuItem(1, "Office") { IsChecked = true }, new HostMenuItem(2, "Home")]));
        using var runner = new FeatureRunner([first, plain, second], _logger);
        runner.Apply(new AppSettings());

        var items = runner.GetMenuItems(100);

        Assert.Equal(4, items.Count);
        Assert.Equal(new HostMenuItem(100, "Seven"), items[0]);
        Assert.True(items[1].IsSeparator);
        Assert.Equal(new HostMenuItem(0, "A note") { IsEnabled = false }, items[2]);
        Assert.Equal("Profiles", items[3].Text);
        Assert.Equal([new HostMenuItem(101, "Office") { IsChecked = true }, new HostMenuItem(102, "Home")], items[3].Items!);
    }

    [Fact]
    public void AChosenItemGoesToItsFeatureWithTheFeaturesOwnId()
    {
        var first = new FakeMenuFeature("first", new HostMenuItem(7, "Seven"));
        var second = new FakeMenuFeature("second", HostMenuItem.Submenu("Profiles", [new HostMenuItem(1, "Office"), new HostMenuItem(2, "Home")]));
        using var runner = new FeatureRunner([first, second], _logger);
        runner.Apply(new AppSettings());
        runner.GetMenuItems(100);

        Assert.True(runner.ChooseMenuItem(102));

        Assert.Empty(first.Chosen);
        Assert.Equal([2], second.Chosen);
        Assert.Equal([(LogLevel.Debug, "The second feature's menu item 2 was chosen.")], _logger.Entries);
    }

    [Fact]
    public void OnlyTheFeaturesItemsOfTheLastMenuCanBeChosen()
    {
        var feature = new FakeMenuFeature("feature", new HostMenuItem(7, "Seven"));
        using var runner = new FeatureRunner([feature], _logger);
        runner.Apply(new AppSettings());
        runner.GetMenuItems(100);

        Assert.False(runner.ChooseMenuItem(1));
        Assert.False(runner.ChooseMenuItem(101));
        Assert.True(runner.ChooseMenuItem(100));
        Assert.False(runner.ChooseMenuItem(100));

        Assert.Equal([7], feature.Chosen);
    }

    [Fact]
    public void AFeatureWhoseMenuItemsFailIsStoppedAndLeftOut()
    {
        var failing = new FakeMenuFeature("failing", new HostMenuItem(1, "One")) { MenuFails = true };
        var other = new FakeMenuFeature("other", new HostMenuItem(1, "One"));
        using var runner = new FeatureRunner([failing, other], _logger);
        runner.Apply(new AppSettings());

        var items = runner.GetMenuItems(100);
        runner.Apply(new AppSettings());

        Assert.Equal([new HostMenuItem(100, "One")], items);
        Assert.Equal(1, failing.DisposeCount);
        Assert.Equal(1, failing.ApplyCount);
        Assert.Equal(
            [(LogLevel.Error, "The failing feature failed, so it's stopped until PC Companion restarts.")],
            _logger.Entries);
    }

    [Fact]
    public void AFeatureWhoseChoiceFailsIsStopped()
    {
        var feature = new FakeMenuFeature("feature", new HostMenuItem(1, "One")) { ChoiceFails = true };
        using var runner = new FeatureRunner([feature], _logger);
        runner.Apply(new AppSettings());
        runner.GetMenuItems(100);

        Assert.True(runner.ChooseMenuItem(100));

        Assert.Equal(1, feature.DisposeCount);
        Assert.Empty(runner.GetMenuItems(100));
        Assert.Contains((LogLevel.Error, "The feature feature failed, so it's stopped until PC Companion restarts."), _logger.Entries);
    }

    [Fact]
    public void AStoppedFeatureAddsNoItems()
    {
        var unsupported = new FakeMenuFeature("unsupported", new HostMenuItem(1, "One")) { Support = FeatureSupport.NotSupported("NeedsWindows11") };
        using var runner = new FeatureRunner([unsupported], _logger);
        runner.Apply(new AppSettings());

        Assert.Empty(runner.GetMenuItems(100));
    }

    [Fact]
    public void AnItemChosenAfterItsFeatureStoppedDoesNothing()
    {
        // Settings can be applied while the menu is open, which runs the message loop.
        var feature = new FakeMenuFeature("feature", new HostMenuItem(1, "One"));
        using var runner = new FeatureRunner([feature], _logger);
        runner.Apply(new AppSettings());
        runner.GetMenuItems(100);
        feature.Fails = true;
        runner.Apply(new AppSettings());

        Assert.True(runner.ChooseMenuItem(100));

        Assert.Empty(feature.Chosen);
        Assert.Contains((LogLevel.Debug, "The feature feature's menu item was chosen after the feature stopped, so it does nothing."), _logger.Entries);
    }

    private sealed class FakeMenuFeature(string id, params HostMenuItem[] items) : IFeature, IHostMenuItems
    {
        public FeatureInfo Info { get; } = new(id, "test", id + "Name", id + "Description", "");

        public FeatureSupport Support { get; init; } = FeatureSupport.Supported;

        public bool Fails { get; set; }

        public bool MenuFails { get; init; }

        public bool ChoiceFails { get; init; }

        public int ApplyCount { get; private set; }

        public int DisposeCount { get; private set; }

        public List<int> Chosen { get; } = [];

        public void Apply(JsonElement? settings)
        {
            ApplyCount++;
            if (Fails)
            {
                throw new InvalidOperationException("The fake feature failed.");
            }
        }

        public IReadOnlyList<HostMenuItem> GetMenuItems() =>
            MenuFails ? throw new InvalidOperationException("The fake feature's menu failed.") : items;

        public void ChooseMenuItem(int id)
        {
            Chosen.Add(id);
            if (ChoiceFails)
            {
                throw new InvalidOperationException("The fake feature's choice failed.");
            }
        }

        public void Dispose() => DisposeCount++;
    }

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
