using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Clipboard.Tests;

[Collection(nameof(ClipboardActionsThreads))]
public sealed class ClipboardActionsFeatureTests : IDisposable
{
    private readonly FakeClipboardActionsSystem _system = new();
    private readonly RecordingLogger<ClipboardActionsFeature> _logger = new();
    private readonly ClipboardActionsFeature _feature;

    public ClipboardActionsFeatureTests()
    {
        _feature = new ClipboardActionsFeature(_system, new FakeHostCommands(), _logger);
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
}
