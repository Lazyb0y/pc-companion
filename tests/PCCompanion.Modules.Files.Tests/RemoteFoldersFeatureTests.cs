using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>Providers that run until a test ends them, with the exit code it gives.</summary>
internal sealed class FakeProviders
{
    private readonly List<TaskCompletionSource<int>> _running = [];
    private readonly Lock _gate = new();

    public int Started { get; private set; }

    public int StopRequests { get; private set; }

    public bool FailToStart { get; set; }

    public RunningProcess Start()
    {
        if (FailToStart)
        {
            throw new Win32Exception(2);
        }

        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            Started++;
            _running.Add(exited);
        }

        return RunningProcess.Watch(1000 + Started, exited.Task);
    }

    /// <summary>The provider stops when asked, as the real one does.</summary>
    public void AskToStop()
    {
        StopRequests++;
        End(0);
    }

    /// <summary>Ends the running providers with <paramref name="exitCode"/>.</summary>
    public void End(int exitCode)
    {
        List<TaskCompletionSource<int>> running;
        lock (_gate)
        {
            running = [.. _running];
            _running.Clear();
        }

        foreach (var exited in running)
        {
            exited.TrySetResult(exitCode);
        }
    }
}

public sealed class RemoteFoldersFeatureTests
{
    private const string Home = "00000000000000000000000000000001";
    private const string Work = "00000000000000000000000000000002";
    private const string Servers = """
        { "enabled": true, "servers": [
          { "id": "00000000000000000000000000000001", "name": "Home", "host": "nas.example.lan", "userName": "ana", "remoteFolder": "/home/ana", "folder": "C:\\Users\\Ana\\Remote Folders\\Home", "hostKey": { "type": "ssh-ed25519", "sha256": "key" } },
          { "id": "00000000000000000000000000000002", "name": "Work", "host": "files.example.com", "userName": "ana", "remoteFolder": "/home/ana", "folder": "C:\\Users\\Ana\\Remote Folders\\Work", "hostKey": { "type": "ssh-ed25519", "sha256": "key" } } ] }
        """;

    private readonly RecordingLogger<RemoteFoldersFeature> _logger = new();
    private readonly FakeProviders _providers = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingNotifications _notifications = new();
    private readonly FakeHostCommands _commands = new();
    private readonly List<RemoteFoldersRequest> _requests = [];
    private RemoteFoldersAnswer? _status;

    private RemoteFoldersFeature NewFeature() =>
        new(_providers.Start, _providers.AskToStop, AskAsync, action => action(), _notifications, _commands, _time, _logger);

    private Task<RemoteFoldersAnswer?> AskAsync(RemoteFoldersRequest request, CancellationToken cancellation)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(request.Kind == RemoteFoldersRequestKind.Status ? _status : new RemoteFoldersAnswer { Done = true });
    }

    private bool WasAsked(RemoteFoldersRequestKind kind)
    {
        lock (_requests)
        {
            return _requests.Any(request => request.Kind == kind);
        }
    }

    private static RemoteServerStatus Status(string id, RemoteServerState state, int waiting = 0, params string[] conflicts) =>
        new() { Id = id, State = state, ChangesWaiting = waiting, Conflicts = conflicts };

    // The feature's window must be disposed on the thread that made it, as the host does on its UI
    // thread, so the tests wait without leaving it.
    private static void WaitUntil(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), "Timed out.");

    [Fact]
    public void WithoutASectionTheFeatureIsOff()
    {
        using var feature = NewFeature();

        feature.Apply(null);

        Assert.False(feature.IsRunning);
        Assert.Equal(0, _providers.Started);
    }

    [Fact]
    public void TurningItOnAndOffStartsAndStopsTheProvider()
    {
        using var feature = NewFeature();

        feature.Apply(Section("""{ "enabled": true }"""));
        Assert.True(feature.IsRunning);
        WaitUntil(() => feature.ProviderProcessId is not null);

        feature.Apply(Section("""{ "enabled": false }"""));
        Assert.False(feature.IsRunning);
        Assert.Equal(1, _providers.StopRequests);
        WaitUntil(() => feature.ProviderProcessId is null);
        Assert.Equal(1, _providers.Started);
    }

    [Fact]
    public void SettingsThatCantBeReadTurnItOffWithAWarning()
    {
        using var feature = NewFeature();
        feature.Apply(Section("""{ "enabled": true }"""));

        feature.Apply(Section("""{ "enabled": true, "servers": 3 }"""));

        Assert.False(feature.IsRunning);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("can't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void ItsDefinitionIsInTheFilesModule()
    {
        Assert.Equal("remoteFolders", RemoteFoldersFeature.Definition.Id);
        Assert.Equal(FilesModule.Id, RemoteFoldersFeature.Definition.ModuleId);
        Assert.True(RemoteFoldersFeature.CurrentSupport.IsSupported);
    }

    [Fact]
    public async Task TheMenuListsTheServersWithTheirStates()
    {
        using var feature = NewFeature();
        feature.Apply(Section(Servers));
        Assert.Contains(ServerText("Home", Strings.RemoteFoldersStateNotRunning), Texts(feature.GetMenuItems()));

        _status = new RemoteFoldersAnswer { Servers = [Status(Home, RemoteServerState.Connected), Status(Work, RemoteServerState.Unreachable, waiting: 2)], Done = true };
        await feature.AskStatusAsync();

        var menu = Assert.Single(feature.GetMenuItems());
        Assert.Equal(Strings.RemoteFoldersName, menu.Text);
        var texts = Texts([menu]);
        Assert.Contains(ServerText("Home", Strings.RemoteFoldersStateConnected), texts);
        Assert.Contains(ServerText("Work", RemoteFoldersFeature.StateText(Status(Work, RemoteServerState.Unreachable, waiting: 2))), texts);
        Assert.Contains("2", RemoteFoldersFeature.StateText(Status(Work, RemoteServerState.Unreachable, waiting: 2)), StringComparison.Ordinal);
        Assert.Contains(Strings.RemoteFoldersMenuSyncNow, texts);
    }

    [Fact]
    public void WithoutServersThereIsNoMenu()
    {
        using var feature = NewFeature();
        feature.Apply(Section("""{ "enabled": true }"""));

        Assert.Empty(feature.GetMenuItems());
    }

    [Fact]
    public async Task SyncNowAsksTheProviderAndOpenFolderOpensIt()
    {
        using var feature = NewFeature();
        feature.Apply(Section(Servers));
        _status = new RemoteFoldersAnswer { Servers = [Status(Home, RemoteServerState.Connected)], Done = true };
        await feature.AskStatusAsync();
        var menu = feature.GetMenuItems()[0].Items!;

        feature.ChooseMenuItem(menu.Single(item => item.Text == Strings.RemoteFoldersMenuSyncNow).Id);
        feature.ChooseMenuItem(menu[1].Items![0].Id);

        WaitUntil(() => WasAsked(RemoteFoldersRequestKind.SyncNow));
        WaitUntil(() => _commands.Programs.Count == 1);
        Assert.Equal([@"C:\Users\Ana\Remote Folders\Work"], _commands.Programs[0].Arguments);
    }

    [Fact]
    public async Task ASignInThatFailsOrAKeyThatChangesIsNotifiedOnce()
    {
        using var feature = NewFeature();
        feature.Apply(Section(Servers));

        _status = new RemoteFoldersAnswer { Servers = [Status(Home, RemoteServerState.SignInFailed), Status(Work, RemoteServerState.KeyChanged)] };
        await feature.AskStatusAsync();
        await feature.AskStatusAsync();

        Assert.Equal(2, _notifications.Shown.Count);
        Assert.Contains("Home", _notifications.Shown[0].Title, StringComparison.Ordinal);
        Assert.Contains("Work", _notifications.Shown[1].Title, StringComparison.Ordinal);
        _notifications.Shown[0].Selected!();
        Assert.Equal(["remoteFolders"], _commands.FeaturePagesOpened);
    }

    [Fact]
    public async Task ANewConflictCopyIsNotifiedAndOpensWithTheCopySelected()
    {
        using var feature = NewFeature();
        feature.Apply(Section(Servers));
        _status = new RemoteFoldersAnswer { Servers = [Status(Home, RemoteServerState.Connected, 0, "docs/Plan (conflict).docx")] };

        await feature.AskStatusAsync();
        await feature.AskStatusAsync();

        var (title, text, selected) = Assert.Single(_notifications.Shown);
        Assert.Equal(Strings.RemoteFoldersConflictTitle, title);
        Assert.Contains("Plan (conflict).docx", text, StringComparison.Ordinal);
        selected!();
        WaitUntil(() => _commands.Programs.Count == 1);
        Assert.Equal(["/select,", @"C:\Users\Ana\Remote Folders\Home\docs\Plan (conflict).docx"], _commands.Programs[0].Arguments);
    }

    [Fact]
    public async Task WhileTheProviderDoesntAnswerTheServersShowAsStarting()
    {
        using var feature = NewFeature();
        feature.Apply(Section(Servers));

        await feature.AskStatusAsync();

        Assert.Contains(ServerText("Home", Strings.RemoteFoldersStateNotRunning), Texts(feature.GetMenuItems()));
        Assert.False(feature.GetMenuItems()[0].Items!.Single(item => item.Text == Strings.RemoteFoldersMenuSyncNow).IsEnabled);
    }

    private static string ServerText(string name, string state) => Format(Strings.RemoteFoldersMenuServer, name, state);

    private static string Format(string format, params object[] values) => string.Format(System.Globalization.CultureInfo.CurrentCulture, format, values);

    private static List<string> Texts(IEnumerable<HostMenuItem> items) =>
        [.. items.SelectMany(item => item.Items is { } children ? [item.Text, .. Texts(children)] : new[] { item.Text })];

    private sealed class RecordingNotifications : IHostNotifications
    {
        public List<(string Title, string Text, Action? Selected)> Shown { get; } = [];

        public bool Show(string title, string text, Action? selected)
        {
            Shown.Add((title, text, selected));
            return true;
        }
    }
}

public sealed class ProviderSupervisorTests : IDisposable
{
    private readonly RecordingLogger<ProviderSupervisor> _logger = new();
    private readonly FakeProviders _providers = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ProviderSupervisor _supervisor;
    private int _gaveUp;

    public ProviderSupervisorTests()
    {
        _supervisor = new ProviderSupervisor(_providers.Start, _providers.AskToStop, _time, _logger);
        _supervisor.GaveUp += (_, _) => Interlocked.Increment(ref _gaveUp);
    }

    public void Dispose() => _supervisor.Dispose();

    // The supervisor works on the thread pool: the clock moves a little at a time until it has done its part.
    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out.");
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private Task RunningAsync(int started) => AdvanceUntilAsync(() => _providers.Started == started && _supervisor.ProcessId is not null);

    [Fact]
    public async Task ItStartsTheProviderOnce()
    {
        _supervisor.Run(settingsChanged: true);
        _supervisor.Run(settingsChanged: false);

        await RunningAsync(1);
        Assert.Equal(1, _providers.Started);
    }

    [Fact]
    public async Task AProviderThatEndsUnexpectedlyStartsAgainAfter1Then10Then60Seconds()
    {
        _supervisor.Run(settingsChanged: true);
        await RunningAsync(1);

        foreach (var (delay, started) in new[] { (1, 2), (10, 3), (60, 4) })
        {
            var ended = _time.Now;
            _providers.End(unchecked((int)0xE0434352));
            await RunningAsync(started);
            Assert.InRange((_time.Now - ended).TotalSeconds, delay, delay + 2);
        }
    }

    [Fact]
    public async Task AfterAFourthCrashWithinAnHourItGivesUpUntilTheSettingsChange()
    {
        _supervisor.Run(settingsChanged: true);
        for (int started = 1; started <= 4; started++)
        {
            await RunningAsync(started);
            _providers.End(1);
        }

        await AdvanceUntilAsync(() => Volatile.Read(ref _gaveUp) == 1);
        Assert.True(_supervisor.HasGivenUp);
        _time.Advance(TimeSpan.FromMinutes(5));
        _supervisor.Run(settingsChanged: false);
        Assert.Equal(4, _providers.Started);

        _supervisor.Run(settingsChanged: true);
        await RunningAsync(5);
        Assert.False(_supervisor.HasGivenUp);
    }

    [Fact]
    public async Task CrashesMoreThanAnHourApartDontAddUp()
    {
        _supervisor.Run(settingsChanged: true);
        for (int started = 1; started <= 6; started++)
        {
            await RunningAsync(started);
            _time.Advance(TimeSpan.FromMinutes(25));
            _providers.End(1);
        }

        await RunningAsync(7);
        Assert.False(_supervisor.HasGivenUp);
    }

    [Fact]
    public async Task AProviderAskedToStopIsntStartedAgain()
    {
        _supervisor.Run(settingsChanged: true);
        await RunningAsync(1);

        _supervisor.Stop();
        await AdvanceUntilAsync(() => _supervisor.ProcessId is null);
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(1, _providers.Started);
        Assert.Equal(1, _providers.StopRequests);
    }

    [Fact]
    public async Task TurningItOffAndOnStartsANewProviderWithoutCountingACrash()
    {
        _supervisor.Run(settingsChanged: true);
        await RunningAsync(1);

        _supervisor.Stop();
        _supervisor.Run(settingsChanged: true);

        await RunningAsync(2);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("unexpectedly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProviderThatCantStartCountsAsACrash()
    {
        _providers.FailToStart = true;
        _supervisor.Run(settingsChanged: true);

        await AdvanceUntilAsync(() => _supervisor.HasGivenUp);
        Assert.Equal(0, _providers.Started);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("couldn't be started", StringComparison.Ordinal));
    }
}
