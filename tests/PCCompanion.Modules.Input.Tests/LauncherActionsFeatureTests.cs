using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.LauncherActions;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

public sealed class LauncherActionsFeatureTests : IDisposable
{
    private const string On = """{ "enabled": true }""";

    private static readonly FeatureAction UseOffice = new("networkProfiles", "use", "Office");
    private static readonly FeatureInfo NetworkProfiles = new("networkProfiles", "network", "NetworkProfilesName", "NetworkProfilesDescription", "");

    private readonly FakeHostActions _actions = new();
    private readonly FakeNotifications _notifications = new();
    private readonly FakeHostCommands _commands = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<LauncherActionsFeature> _logger = new();
    private readonly List<FakeLauncherSystem> _systems = [];
    private readonly LauncherActionsFeature _feature;

    public LauncherActionsFeatureTests() =>
        _feature = new LauncherActionsFeature(_actions, _notifications, _commands, _time, _logger, () =>
        {
            var system = new FakeLauncherSystem();
            _systems.Add(system);
            return system;
        });

    private FakeLauncherSystem System => _systems[^1];

    public void Dispose() => _feature.Dispose();

    [Fact]
    public void TheDefinitionNamesTheFeatureAndItsModule()
    {
        Assert.Equal("launcherActions", LauncherActionsFeature.Definition.Id);
        Assert.Equal(InputModule.Id, LauncherActionsFeature.Definition.ModuleId);
    }

    [Fact]
    public void ItsOwnActionsAreLockTurnOffDisplaysAndStartTheScreenSaver()
    {
        Assert.Equal(
            [new FeatureAction("launcherActions", "lock"), new FeatureAction("launcherActions", "turnOffDisplays"), new FeatureAction("launcherActions", "screenSaver")],
            LauncherActionsFeature.OwnActions().Select(offered => offered.Action));
        Assert.All(LauncherActionsFeature.OwnActions(), offered => Assert.NotEmpty(offered.Name));
    }

    [Fact]
    public void ItTakesRequestsOnlyWhileItsOn()
    {
        _feature.Apply(null);
        Assert.Empty(_systems);

        _feature.Apply(Section(On));
        Assert.True(_feature.IsRunning);
        Assert.NotNull(System.Answer);

        _feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(_feature.IsRunning);
        Assert.True(System.PipeDisposed);
        Assert.True(System.IsDisposed);

        _feature.Apply(Section(On));
        Assert.Equal(2, _systems.Count);
    }

    [Fact]
    public void ApplyingTheSettingsAgainKeepsThePipe()
    {
        _feature.Apply(Section(On));
        _feature.Apply(Section("""{ "enabled": true, "showInStart": false }"""));

        Assert.Single(_systems);
        Assert.False(System.PipeDisposed);
    }

    [Fact]
    public void SettingsThatCantBeReadTurnItOff()
    {
        _feature.Apply(Section("""{ "enabled": true, "hiddenFeatures": 3 }"""));

        Assert.Empty(_systems);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AnActionALauncherCanRunRunsOnTheUiThread()
    {
        _actions.Runnable.Add(UseOffice);
        _feature.Apply(Section(On));

        var answer = Ask(UseOffice);
        Assert.False(answer.IsCompleted);
        System.RunPosted();

        Assert.Equal(LauncherResult.Ran, LauncherPipe.ReadAnswer(await answer));
        Assert.Equal([UseOffice], _actions.Ran);
        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task AnActionOfAFeatureThatsOffIsRefusedWithANotificationThatOpensItsPage()
    {
        _actions.Features.Add(NetworkProfiles.Id, NetworkProfiles);
        _feature.Apply(Section(On));

        var answer = Ask(UseOffice);
        System.RunPosted();

        Assert.Equal(LauncherResult.Refused, LauncherPipe.ReadAnswer(await answer));
        Assert.Empty(_actions.Ran);
        Assert.Equal([(Strings.NetworkProfilesName, Format(Strings.LauncherActionsNotificationOff, Strings.NetworkProfilesName))], _notifications.Shown);
        _notifications.Select();
        Assert.Equal(["networkProfiles"], _commands.FeaturePagesOpened);
    }

    [Fact]
    public async Task AnActionTheRunningFeatureRefusesSaysToCheckItsPage()
    {
        _actions.Features.Add(NetworkProfiles.Id, NetworkProfiles);
        _actions.Running.Add(NetworkProfiles.Id);
        _feature.Apply(Section(On));

        var answer = Ask(UseOffice);
        System.RunPosted();

        Assert.Equal(LauncherResult.Refused, LauncherPipe.ReadAnswer(await answer));
        Assert.Equal([(Strings.NetworkProfilesName, Format(Strings.LauncherActionsNotificationRefused, Strings.NetworkProfilesName))], _notifications.Shown);
        _notifications.Select();
        Assert.Equal(["networkProfiles"], _commands.FeaturePagesOpened);
    }

    [Theory]
    [InlineData("noSuchFeature", "use", null)]
    [InlineData("launcherActions", "restart", null)]
    [InlineData("launcherActions", "lock", "Office")]
    public async Task AnActionPCCompanionDoesntHaveIsRefusedWithANotificationThatOpensThisPage(string featureId, string actionId, string? target)
    {
        _feature.Apply(Section(On));

        var answer = Ask(new FeatureAction(featureId, actionId, target));
        System.RunPosted();

        Assert.Equal(LauncherResult.Refused, LauncherPipe.ReadAnswer(await answer));
        Assert.Equal([(Strings.LauncherActionsName, Format(Strings.LauncherActionsNotificationUnknown, Strings.LauncherActionsName))], _notifications.Shown);
        Assert.Equal(0, System.Locks);
        _notifications.Select();
        Assert.Equal(["launcherActions"], _commands.FeaturePagesOpened);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"feature":"networkProfiles","action":"use","target":""}""")]
    [InlineData("""{"feature":"network profiles","action":"use"}""")]
    public async Task ARequestThatIsntOneRunsNothingAndShowsNothing(string request)
    {
        _actions.Runnable.Add(UseOffice);
        _feature.Apply(Section(On));

        var answer = await System.Answer!(Encoding.UTF8.GetBytes(request));

        Assert.Equal(LauncherResult.Invalid, LauncherPipe.ReadAnswer(answer));
        System.RunPosted();
        Assert.Empty(_actions.Ran);
        Assert.Empty(_notifications.Shown);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RequestsRunOneAtATimeInTheOrderTheyArrive()
    {
        var useHome = UseOffice with { Target = "Home" };
        _actions.Runnable.UnionWith([UseOffice, useHome]);
        _feature.Apply(Section(On));

        var first = Ask(useHome);
        var second = Ask(UseOffice);
        System.RunPosted();

        await Task.WhenAll(first, second);
        Assert.Equal([useHome, UseOffice], _actions.Ran);
    }

    [Fact]
    public async Task LockRunsAtOnce()
    {
        _feature.Apply(Section(On));

        var answer = Ask(new FeatureAction("launcherActions", "lock"));
        System.RunPosted();

        Assert.Equal(LauncherResult.Ran, LauncherPipe.ReadAnswer(await answer));
        Assert.Equal(1, System.Locks);
    }

    [Theory]
    [InlineData("turnOffDisplays")]
    [InlineData("screenSaver")]
    public async Task TurningOffTheDisplaysOrStartingTheScreenSaverWaitsUntilNothingIsHeldAndHalfASecondMore(string actionId)
    {
        _feature.Apply(Section(On));
        System.InputHeld = true;

        var answer = Ask(new FeatureAction("launcherActions", actionId));
        System.RunPosted();
        Assert.Equal(LauncherResult.Ran, LauncherPipe.ReadAnswer(await answer));

        Pass(TimeSpan.FromSeconds(2));
        Assert.Equal(0, System.DisplaysOff + System.ScreenSavers);

        System.InputHeld = false;
        Pass(TimeSpan.FromMilliseconds(50));
        Pass(TimeSpan.FromMilliseconds(450));
        Assert.Equal(0, System.DisplaysOff + System.ScreenSavers);

        Pass(TimeSpan.FromMilliseconds(100));
        Assert.Equal(actionId == "turnOffDisplays" ? (1, 0) : (0, 1), (System.DisplaysOff, System.ScreenSavers));
    }

    [Fact]
    public void TheDisplaysTurnOffAfterTenSecondsOfHoldingAKey()
    {
        _feature.Apply(Section(On));
        System.InputHeld = true;

        _ = Ask(new FeatureAction("launcherActions", "turnOffDisplays"));
        System.RunPosted();
        Pass(TimeSpan.FromSeconds(9.9));
        Assert.Equal(0, System.DisplaysOff);

        Pass(TimeSpan.FromSeconds(0.7));
        Assert.Equal(1, System.DisplaysOff);
    }

    [Fact]
    public void ALaterWaitReplacesAnEarlierOne()
    {
        _feature.Apply(Section(On));

        _ = Ask(new FeatureAction("launcherActions", "turnOffDisplays"));
        _ = Ask(new FeatureAction("launcherActions", "screenSaver"));
        System.RunPosted();
        Pass(TimeSpan.FromSeconds(1));

        Assert.Equal((0, 1), (System.DisplaysOff, System.ScreenSavers));
    }

    [Fact]
    public void TurningItOffLeavesARequestWaitingForTheUiThreadUnansweredAndUnrun()
    {
        _actions.Runnable.Add(UseOffice);
        _feature.Apply(Section(On));
        var system = System;

        var answer = Ask(UseOffice);
        _feature.Apply(Section("""{ "enabled": false }"""));
        system.RunPosted();

        Assert.False(answer.IsCompleted);
        Assert.Empty(_actions.Ran);
    }

    [Fact]
    public void TurningItOffEndsAWaitBeforeTheDisplaysTurnOff()
    {
        _feature.Apply(Section(On));
        var system = System;
        _ = Ask(new FeatureAction("launcherActions", "turnOffDisplays"));
        system.RunPosted();

        _feature.Apply(Section("""{ "enabled": false }"""));
        _time.Advance(TimeSpan.FromSeconds(1));
        system.RunPosted();

        Assert.Equal(0, system.DisplaysOff);
    }

    [Fact]
    public void APipeThatStopsWorkingTurnsItOffUntilPCCompanionRestarts()
    {
        _feature.Apply(Section(On));

        System.Failed!(new IOException("The pipe broke."));
        System.RunPosted();

        Assert.False(_feature.IsRunning);
        Assert.True(System.PipeDisposed);
        _feature.Apply(Section(On));
        Assert.Single(_systems);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("pipe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheLogNamesTheActionButNotItsTarget()
    {
        _actions.Runnable.Add(UseOffice);
        _feature.Apply(Section(On));

        var answer = Ask(UseOffice);
        System.RunPosted();
        await answer;

        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("networkProfiles", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Office", StringComparison.Ordinal));
    }

    private static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);

    private Task<byte[]> Ask(FeatureAction action) => System.Answer!(LauncherPipe.WriteRequest(action));

    // Moves the clock in the release wait's steps, running what each step posts.
    private void Pass(TimeSpan time)
    {
        var step = TimeSpan.FromMilliseconds(50);
        for (var passed = TimeSpan.Zero; passed < time; passed += step)
        {
            _time.Advance(step);
            System.RunPosted();
        }
    }

    private sealed class FakeLauncherSystem : ILauncherActionsSystem
    {
        private readonly Queue<Action> _posted = new();

        public Func<byte[], Task<byte[]>>? Answer { get; private set; }

        public Action<Exception>? Failed { get; private set; }

        public bool PipeDisposed { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool InputHeld { get; set; }

        public int Locks { get; private set; }

        public int DisplaysOff { get; private set; }

        public int ScreenSavers { get; private set; }

        public IDisposable Serve(Func<byte[], Task<byte[]>> answer, Action<Exception> failed)
        {
            Answer = answer;
            Failed = failed;
            return new Pipe(this);
        }

        // Like a window's queue, what was posted before the system was disposed still runs.
        public bool Post(Action action)
        {
            if (IsDisposed)
            {
                return false;
            }

            _posted.Enqueue(action);
            return true;
        }

        public void RunPosted()
        {
            while (_posted.TryDequeue(out var action))
            {
                action();
            }
        }

        public void Lock() => Locks++;

        public void TurnOffDisplays() => DisplaysOff++;

        public void StartScreenSaver() => ScreenSavers++;

        public bool IsInputHeld() => InputHeld;

        public void Dispose() => IsDisposed = true;

        private sealed class Pipe(FakeLauncherSystem system) : IDisposable
        {
            public void Dispose() => system.PipeDisposed = true;
        }
    }

    private sealed class FakeHostActions : IHostActions
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public HashSet<FeatureAction> Runnable { get; } = [];

        public Dictionary<string, FeatureInfo> Features { get; } = [];

        public HashSet<string> Running { get; } = [];

        public List<FeatureAction> Ran { get; } = [];

        public bool CanRun(FeatureAction action) => Runnable.Contains(action);

        public bool CanRunFromLauncher(FeatureAction action) => Runnable.Contains(action);

        public bool IsRunning(string featureId) => Running.Contains(featureId);

        public FeatureInfo? FindFeature(string featureId) => Features.GetValueOrDefault(featureId);

        public void Run(FeatureAction action) => Ran.Add(action);
    }

    private sealed class FakeNotifications : IHostNotifications
    {
        private Action? _selected;

        public List<(string Title, string Text)> Shown { get; } = [];

        public bool Show(string title, string text, Action? selected)
        {
            Shown.Add((title, text));
            _selected = selected;
            return true;
        }

        public void Select() => _selected?.Invoke();
    }
}
