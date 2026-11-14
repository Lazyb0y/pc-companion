using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

// How FeatureRunner and HostActions pass actions from shortcuts, mouse buttons and launchers to the
// features that offer them.
public sealed class FeatureActionsTests
{
    private static readonly FeatureAction HideOthers = new("hideApps", "hideOthers");
    private static readonly FeatureAction UseOffice = new("networkProfiles", "use", "Office");

    private readonly RecordingLogger<FeatureRunner> _logger = new();

    // Stands in for the host's UI thread: actions wait until the test runs them.
    private readonly Queue<Action> _uiThread = new();

    [Fact]
    public void AnActionRunsThroughTheFeatureThatOffersIt()
    {
        var hideApps = new FakeActionsFeature("hideApps", HideOthers);
        var network = new FakeActionsFeature("networkProfiles", UseOffice);
        using var runner = new FeatureRunner([hideApps, network], _logger);

        Assert.True(runner.Run(UseOffice));

        Assert.Empty(hideApps.Ran);
        Assert.Equal([UseOffice], network.Ran);
    }

    [Fact]
    public void AFeatureSaysWhichOfItsActionsCanRun()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("networkProfiles", UseOffice)], _logger);

        Assert.True(runner.CanRun(UseOffice));
        Assert.False(runner.CanRun(UseOffice with { Target = "Home" }));
        Assert.False(runner.CanRun(HideOthers));
    }

    [Fact]
    public void AFeatureWithoutActionsOffersNone()
    {
        using var runner = new FeatureRunner([new PlainFeature("hideApps")], _logger);

        Assert.False(runner.CanRun(HideOthers));
        Assert.False(runner.Run(HideOthers));
    }

    [Fact]
    public void AFeatureWhoseActionFailsIsStopped()
    {
        var hideApps = new FakeActionsFeature("hideApps", HideOthers) { RunFails = true };
        using var runner = new FeatureRunner([hideApps], _logger);

        Assert.True(runner.Run(HideOthers));

        Assert.Equal(1, hideApps.DisposeCount);
        Assert.False(runner.CanRun(HideOthers));
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("hideApps", StringComparison.Ordinal));
    }

    [Fact]
    public void AFeatureWhoseAnswerFailsIsStoppedAndCantRunIt()
    {
        var hideApps = new FakeActionsFeature("hideApps", HideOthers) { CanRunFails = true };
        using var runner = new FeatureRunner([hideApps], _logger);

        Assert.False(runner.CanRun(HideOthers));

        Assert.Equal(1, hideApps.DisposeCount);
        Assert.False(runner.Run(HideOthers));
    }

    [Fact]
    public void TheLogNamesTheActionButNotWhatItActsOn()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("networkProfiles", UseOffice)], _logger);

        runner.Run(UseOffice);

        var entry = Assert.Single(_logger.Entries);
        Assert.Contains("networkProfiles", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Office", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALauncherCanRunAnActionThatDoesntActOnTheWindowInFront()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("networkProfiles", UseOffice)], _logger);

        Assert.True(runner.CanRunFromLauncher(UseOffice));
        Assert.False(runner.CanRunFromLauncher(UseOffice with { Target = "Home" }));
        Assert.False(runner.CanRunFromLauncher(HideOthers));
    }

    [Fact]
    public void ALauncherCantRunAnActionOnTheWindowInFront()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("hideApps", HideOthers) { InFront = true }], _logger);

        Assert.True(runner.CanRun(HideOthers));
        Assert.False(runner.CanRunFromLauncher(HideOthers));
    }

    [Fact]
    public void AFeatureWhoseAnswerToALauncherFailsIsStopped()
    {
        var network = new FakeActionsFeature("networkProfiles", UseOffice) { CanRunFails = true };
        using var runner = new FeatureRunner([network], _logger);

        Assert.False(runner.CanRunFromLauncher(UseOffice));

        Assert.Equal(1, network.DisposeCount);
    }

    [Fact]
    public void AFeatureRunsWhileItsSectionTurnsItOnUntilItFails()
    {
        var network = new FakeActionsFeature("networkProfiles", UseOffice) { RunFails = true };
        using var runner = new FeatureRunner([network], _logger);
        Assert.False(runner.IsRunning("networkProfiles"));

        runner.Apply(Settings("networkProfiles", """{ "enabled": true }"""));
        Assert.True(runner.IsRunning("networkProfiles"));
        Assert.False(runner.IsRunning("hideApps"));

        runner.Apply(Settings("networkProfiles", """{ "enabled": false }"""));
        Assert.False(runner.IsRunning("networkProfiles"));

        runner.Apply(Settings("networkProfiles", """{ "enabled": true }"""));
        runner.Run(UseOffice);
        Assert.False(runner.IsRunning("networkProfiles"));
    }

    [Fact]
    public void AFeatureThisPcDoesntSupportDoesntRun()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("networkProfiles", UseOffice) { IsSupported = false }], _logger);

        runner.Apply(Settings("networkProfiles", """{ "enabled": true }"""));

        Assert.False(runner.IsRunning("networkProfiles"));
    }

    [Fact]
    public void BeforeTheHostAttachesNoActionCanRun()
    {
        var actions = new HostActions();

        Assert.False(actions.CanRun(HideOthers));
        Assert.False(actions.CanRunFromLauncher(HideOthers));
        Assert.False(actions.IsRunning("hideApps"));
        actions.Run(HideOthers);
    }

    [Fact]
    public void TheHostRunsActionsOnItsUiThread()
    {
        var hideApps = new FakeActionsFeature("hideApps", HideOthers);
        using var runner = new FeatureRunner([hideApps], _logger);
        var actions = new HostActions();
        actions.Attach(runner, _uiThread.Enqueue);

        actions.Run(HideOthers);
        Assert.Empty(hideApps.Ran);
        _uiThread.Dequeue()();

        Assert.Equal([HideOthers], hideApps.Ran);
        Assert.True(actions.CanRun(HideOthers));
        Assert.True(actions.CanRunFromLauncher(HideOthers));
    }

    [Fact]
    public void TheHostSaysWhichFeaturesRun()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("hideApps", HideOthers)], _logger);
        var actions = new HostActions();
        actions.Attach(runner, _uiThread.Enqueue);

        runner.Apply(Settings("hideApps", """{ "enabled": true }"""));

        Assert.True(actions.IsRunning("hideApps"));
    }

    [Fact]
    public void AttachingAndApplyingTheSettingsSayThatActionsMayHaveChanged()
    {
        using var runner = new FeatureRunner([new FakeActionsFeature("hideApps", HideOthers)], _logger);
        var actions = new HostActions();
        var changes = 0;
        actions.Changed += (_, _) => changes++;

        actions.Attach(runner, _uiThread.Enqueue);
        runner.Apply(new AppSettings());

        Assert.Equal(2, changes);
    }

    private static AppSettings Settings(string featureId, string section)
    {
        using var document = JsonDocument.Parse(section);
        return new AppSettings { Features = new Dictionary<string, JsonElement> { [featureId] = document.RootElement.Clone() } };
    }

    private sealed class FakeActionsFeature(string id, FeatureAction offered) : IFeature, IFeatureActions
    {
        public FeatureInfo Info { get; } = new(id, "test", id + "Name", id + "Description", "");

        public FeatureSupport Support => IsSupported ? FeatureSupport.Supported : FeatureSupport.NotSupported("TestNotSupported");

        public bool IsSupported { get; init; } = true;

        // Whether its action acts on the window in front.
        public bool InFront { get; init; }

        public bool RunFails { get; init; }

        public bool CanRunFails { get; init; }

        public List<FeatureAction> Ran { get; } = [];

        public int DisposeCount { get; private set; }

        public void Apply(JsonElement? settings)
        {
        }

        public bool CanRun(FeatureAction action) =>
            CanRunFails ? throw new InvalidOperationException("The fake feature's answer failed.") : action == offered;

        public bool OnWindowInFront(FeatureAction action) => InFront && action == offered;

        public void Run(FeatureAction action)
        {
            Ran.Add(action);
            if (RunFails)
            {
                throw new InvalidOperationException("The fake feature's action failed.");
            }
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class PlainFeature(string id) : IFeature
    {
        public FeatureInfo Info { get; } = new(id, "test", id + "Name", id + "Description", "");

        public FeatureSupport Support => FeatureSupport.Supported;

        public void Apply(JsonElement? settings)
        {
        }

        public void Dispose()
        {
        }
    }
}
