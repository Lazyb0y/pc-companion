using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Clipboard.Tests;

[Collection(nameof(ClipboardActionsThreads))]
public sealed class ClipboardActionsFeatureTests : IDisposable
{
    private readonly FakeClipboardActionsSystem _system = new();
    private readonly RecordingLogger<ClipboardActionsFeature> _logger = new();
    private readonly ShortcutRefusals _refusals = new();
    private readonly ClipboardActionsFeature _feature;

    public ClipboardActionsFeatureTests()
    {
        _feature = new ClipboardActionsFeature(_system, new FakeHostCommands(), _refusals, _logger);
    }

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheFeatureIsOffWithoutASection()
    {
        _feature.Apply(null);

        Assert.False(_feature.IsRunning);
        Assert.Empty(_system.Events);
    }

    [Fact]
    public void ASecondDisposeDoesntWaitAgain()
    {
        using var gate = new ManualResetEventSlim();
        _system.DisposeGate = gate;
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_system.WaitUntil(system => system.Events.Contains("register")));

        // As the host does at exit: its feature runner, then its services.
        _feature.Dispose();
        _feature.Dispose();

        Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        gate.Set();
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Contains((LogLevel.Debug, "Clipboard Actions' thread ended."))));
    }

    [Fact]
    public void RefusedShortcutsAreListedForTheSettingsAppUntilTheFeatureStops()
    {
        _system.RefusesShortcut = true;
        _feature.Apply(Section("""{ "enabled": true, "selectedText": true }"""));

        Assert.True(_system.WaitUntil(_ => _refusals.All.Count > 0));
        Assert.Equal([new RefusedShortcut("clipboardActions", ClipboardActionsFeature.CopiedTextShortcut)], _refusals.All);

        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.True(_system.WaitUntil(_ => _refusals.All.Count == 0));
    }

    [Fact]
    public void TurningItOnRegistersTheShortcutAndTurningItOffRemovesIt()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_feature.IsRunning);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("register")));

        _feature.Apply(Section("""{ "enabled": false }"""));

        Assert.False(_feature.IsRunning);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("dispose shortcut")));
    }

    [Fact]
    public void ASectionThatCantBeReadTurnsTheFeatureOff()
    {
        _feature.Apply(Section("""{ "enabled": true }"""));

        _feature.Apply(Section("""{ "enabled": "yes" }"""));

        Assert.False(_feature.IsRunning);
        Assert.Contains((LogLevel.Warning, "The Clipboard Actions settings can't be read, so Clipboard Actions is off."), _logger.Entries);
    }

    [Fact]
    public void AfterAFailureTheFeatureStaysOff()
    {
        _system.ReadFailure = new InvalidOperationException("Can't read.");
        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_system.WaitUntil(system => system.Shortcut is not null));

        _system.Shortcut!.Press();
        Assert.True(_system.WaitUntil(system => system.Events.Contains("dispose shortcut")));
        _feature.Apply(Section("""{ "enabled": true, "openLinks": false }"""));

        Assert.False(_feature.IsRunning);
        Assert.Equal(1, _system.Events.Count(happened => happened == "register"));
    }

    [Fact]
    public void TheDefinitionNamesTheFeatureAndItsModule()
    {
        Assert.Equal("clipboardActions", ClipboardActionsFeature.Definition.Id);
        Assert.Equal(ClipboardModule.Id, ClipboardActionsFeature.Definition.ModuleId);
    }

    [Fact]
    public void ItOffersTheMenuForTheSelectedTextOnlyWithSelectedTextOn()
    {
        var copied = new FeatureAction("clipboardActions", "copiedText");
        var selected = new FeatureAction("clipboardActions", "selectedText");

        Assert.Equal([copied], ClipboardActionsFeature.OfferedActions(new ClipboardActionsSettings()).Select(offered => offered.Action));
        Assert.Equal([copied, selected], ClipboardActionsFeature.OfferedActions(new ClipboardActionsSettings { SelectedText = true }).Select(offered => offered.Action));
    }

    [Fact]
    public void ItSaysWhichKeysItsShortcutsUseAndTheSelectedTextsOnlyWithSelectedTextOn()
    {
        const HotKeyModifiers WindowsAlt = HotKeyModifiers.Windows | HotKeyModifiers.Alt;
        var copied = (new FeatureAction("clipboardActions", "copiedText"), ShortcutKeys.OfVirtualKey(WindowsAlt, 0x56));
        var selected = (new FeatureAction("clipboardActions", "selectedText"), ShortcutKeys.OfVirtualKey(WindowsAlt, 0x53));

        Assert.Equal([copied], ClipboardActionsFeature.Shortcuts(new ClipboardActionsSettings()));
        Assert.Equal([copied, selected], ClipboardActionsFeature.Shortcuts(new ClipboardActionsSettings { SelectedText = true }));
        Assert.Equal([selected], ClipboardActionsFeature.Shortcuts(new ClipboardActionsSettings { SelectedText = true, Shortcut = ClipboardActionsShortcut.None }));
        Assert.Empty(ClipboardActionsFeature.Shortcuts(new ClipboardActionsSettings { SelectedText = true, Shortcut = ClipboardActionsShortcut.None, SelectedTextShortcut = ClipboardActionsSelectionShortcut.None }));
    }

    [Fact]
    public void ItCanRunItsActionsWhileItsOn()
    {
        var copied = new FeatureAction("clipboardActions", "copiedText");
        var selected = new FeatureAction("clipboardActions", "selectedText");
        Assert.False(_feature.CanRun(copied));

        _feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(_feature.CanRun(copied));
        Assert.False(_feature.CanRun(selected));
        Assert.False(_feature.CanRun(copied with { Target = "Notes" }));

        _feature.Apply(Section("""{ "enabled": true, "selectedText": true }"""));
        Assert.True(_feature.CanRun(selected));
    }
}
