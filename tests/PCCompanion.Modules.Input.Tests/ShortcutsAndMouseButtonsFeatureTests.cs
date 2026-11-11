using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

// Starting the feature creates its own hidden window on a thread of its own. The tests use mouse
// buttons, which this feature doesn't take yet, and a shortcut whose action can't run, so nothing
// registers a shortcut.
public sealed class ShortcutsAndMouseButtonsFeatureTests : IDisposable
{
    private const string HideOthersOnBack = """{ "action": { "feature": "hideApps", "id": "hideOthers" }, "mouseButton": { "button": "Back", "modifiers": [] } }""";
    private const string LockOnForward = """{ "action": { "feature": "shortcutsAndMouseButtons", "id": "lock" }, "mouseButton": { "button": "Forward", "modifiers": [] } }""";
    private const string UseOfficeOnCtrlAltO = """{ "action": { "feature": "networkProfiles", "id": "use", "target": "Office" }, "shortcut": { "modifiers": [ "Control", "Alt" ], "key": "KeyO" } }""";
    private const string NoInput = """{ "action": { "feature": "hideApps", "id": "hideApp" } }""";

    private readonly FakeHostActions _actions = new();
    private readonly ShortcutRefusals _refusals = new();
    private readonly RecordingLogger<ShortcutsAndMouseButtonsFeature> _logger = new();
    private readonly ShortcutsAndMouseButtonsFeature _feature;

    public ShortcutsAndMouseButtonsFeatureTests()
    {
        _feature = new ShortcutsAndMouseButtonsFeature(_actions, _refusals, TimeProvider.System, _logger);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsInTheInputModule()
    {
        Assert.Equal(("shortcutsAndMouseButtons", "input"), (_feature.Info.Id, _feature.Info.ModuleId));
        Assert.True(_feature.Support.IsSupported);
    }

    [Fact]
    public void WithoutASectionTheFeatureStaysOff()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Null(_feature.CurrentPlan);
    }

    [Fact]
    public void ItPlansTheTriggersWhoseActionsCanRunAndItsOwnActions()
    {
        _actions.Runnable.Add(new FeatureAction("hideApps", "hideOthers"));

        _feature.Apply(On(HideOthersOnBack, LockOnForward, UseOfficeOnCtrlAltO));

        Assert.True(_feature.IsRunning);
        Assert.Equal([0, 1], _feature.CurrentPlan!.Triggers.Select(trigger => trigger.Index));
        Assert.Equal(TriggerButton.Back, _feature.CurrentPlan.Triggers[0].Input.Button);
    }

    [Fact]
    public void WhenTheHostAppliedTheSettingsTheActionsThatCanRunAreAskedAgain()
    {
        _feature.Apply(On(HideOthersOnBack, LockOnForward));
        Assert.Single(_feature.CurrentPlan!.Triggers);

        _actions.Runnable.Add(new FeatureAction("hideApps", "hideOthers"));
        _actions.RaiseChanged();

        Assert.Equal(2, _feature.CurrentPlan!.Triggers.Count);
    }

    [Fact]
    public void ATriggerThatCantBeUsedIsLeftOutWithAWarning()
    {
        _feature.Apply(On(NoInput, LockOnForward));

        Assert.Equal([1], _feature.CurrentPlan!.Triggers.Select(trigger => trigger.Index));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Trigger 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ATriggerThatCantBeUsedIsWarnedAboutOncePerChange()
    {
        _feature.Apply(On(NoInput, LockOnForward));
        _actions.RaiseChanged();
        _feature.Apply(On(NoInput, LockOnForward));

        Assert.Single(_logger.Entries, entry => entry.Message.Contains("Trigger 1", StringComparison.Ordinal));

        _feature.Apply(On(LockOnForward, NoInput));

        Assert.Single(_logger.Entries, entry => entry.Message.Contains("Trigger 2", StringComparison.Ordinal));
    }

    [Fact]
    public void ItsOwnActionsAreOffered()
    {
        Assert.Equal(
            ["lock", "turnOffDisplays", "screenSaver"],
            ShortcutsAndMouseButtonsFeature.OfferedActions().Select(offered => offered.Action.ActionId));
    }

    [Fact]
    public void TurningItOffStopsItAndClearsThePlan()
    {
        _feature.Apply(On(LockOnForward));

        _feature.Apply(Section("""{ "enabled": false }"""));
        _actions.RaiseChanged();

        Assert.False(_feature.IsRunning);
        Assert.Null(_feature.CurrentPlan);
    }

    [Fact]
    public void AnUnreadableSectionTurnsItOffWithAWarning()
    {
        _feature.Apply(On(LockOnForward));

        _feature.Apply(Section("""{ "enabled": true, "triggers": 3 }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    private static System.Text.Json.JsonElement On(params string[] triggers) =>
        Section($$"""{ "enabled": true, "triggers": [{{string.Join(",", triggers)}}] }""");

    private sealed class FakeHostActions : IHostActions
    {
        public event EventHandler? Changed;

        public HashSet<FeatureAction> Runnable { get; } = [];

        public List<FeatureAction> Ran { get; } = [];

        public bool CanRun(FeatureAction action) => Runnable.Contains(action);

        public void Run(FeatureAction action) => Ran.Add(action);

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
