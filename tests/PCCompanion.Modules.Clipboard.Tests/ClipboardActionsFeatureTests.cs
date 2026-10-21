using System;
using System.Linq;
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
