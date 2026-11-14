using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
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
    private readonly RecordingLogger<RemoteFoldersFeature> _logger = new();
    private readonly FakeProviders _providers = new();
    private readonly ManualTimeProvider _time = new();

    private RemoteFoldersFeature NewFeature() =>
        new(_providers.Start, _providers.AskToStop, new NoNotifications(), new FakeHostCommands(), _time, _logger);

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

    private sealed class NoNotifications : IHostNotifications
    {
        public bool Show(string title, string text, Action? selected) => true;
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
