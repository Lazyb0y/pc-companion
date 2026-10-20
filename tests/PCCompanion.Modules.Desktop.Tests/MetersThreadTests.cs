using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Desktop.SystemMeters;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// The meters thread uses one window class name, so its tests run one at a time.
[CollectionDefinition(nameof(MetersThreads))]
public sealed class MetersThreads;

// Each test runs the meters thread with its own hidden window and a real timer, and fake icons,
// so nothing shows on the taskbar, no menu opens and Task Manager doesn't start.
[Collection(nameof(MetersThreads))]
public sealed class MetersThreadTests : IDisposable
{
    private const uint SessionChangeMessage = 0x02B1; // WM_WTSSESSION_CHANGE
    private const nuint SessionLock = 0x7;
    private const nuint SessionUnlock = 0x8;

    private static readonly SystemMetersSettings AllMeters = new() { Enabled = true };

    private readonly FakeMetersSystem _system = new();
    private readonly FakeHostCommands _commands = new();
    private readonly RecordingLogger<MetersThreadTests> _logger = new();
    private readonly List<MetersThread> _threads = [];

    public void Dispose()
    {
        foreach (var thread in _threads)
        {
            StopAndJoin(thread);
        }
    }

    [Fact]
    public void TheMetersShowOnTheirOwnThreadAndAreRemovedWhenItStops()
    {
        var thread = Start(AllMeters with { Cpu = false });
        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Memory") && system.Events.Contains("show Network")));

        StopAndJoin(thread);

        Assert.DoesNotContain("show Cpu", _system.Events);
        Assert.Equal(["dispose", "remove Memory", "remove Network"], _system.Events.TakeLast(3).Order(StringComparer.Ordinal));
        Assert.Equal("dispose", _system.Events[^1]);
        Assert.False(thread.HasFailed);
    }

    [Fact]
    public void NewSettingsReachTheThread()
    {
        var thread = Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Network")));

        thread.Update(AllMeters with { Network = false });

        Assert.True(_system.WaitUntil(system => system.Events.Contains("remove Network")));
    }

    [Fact]
    public void AThreadStoppedAtOnceShowsNothing()
    {
        var thread = Start(AllMeters);

        StopAndJoin(thread);

        Assert.DoesNotContain(_system.Events, text => text.StartsWith("show", StringComparison.Ordinal));
        Assert.False(thread.HasFailed);
    }

    [Fact]
    public void ANewThreadWaitsForTheOneBefore()
    {
        var first = Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Memory")));
        first.Stop();

        var second = Start(AllMeters, first);

        Assert.True(_system.WaitUntil(system => system.Events.Count(text => text == "show Memory") == 2));
        Assert.False(second.HasFailed);
    }

    [Fact]
    public void SelectingAMeterOpensTaskManager()
    {
        Start(AllMeters);

        _system.OnMetersThread(icons => icons.Select());

        Assert.True(_system.WaitUntil(system => system.TaskManagerOpened == 1));
    }

    [Theory]
    [InlineData(MeterMenuCommand.TaskManager, 1, 0, 0)]
    [InlineData(MeterMenuCommand.Settings, 0, 1, 0)]
    [InlineData(MeterMenuCommand.Exit, 0, 0, 1)]
    [InlineData(MeterMenuCommand.None, 0, 0, 0)]
    public void TheMenuRunsTheCommandChosen(MeterMenuCommand command, int taskManager, int settings, int exits)
    {
        _system.MenuChoice = command;
        Start(AllMeters);

        _system.OnMetersThread(icons => icons.OpenMenu());

        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message == $"Closed the meters' menu; chose {command}.")));
        Assert.Equal((taskManager, settings, exits), (_system.TaskManagerOpened, _commands.SettingsOpened, _commands.Exits));
    }

    [Fact]
    public void UpdatesPauseWhileTheSessionIsLocked()
    {
        Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Updates >= 1));

        _system.Window!.Post(SessionChangeMessage, SessionLock);
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.StartsWith("System Meters paused: True", StringComparison.Ordinal))));
        var updates = _system.Updates;
        Assert.False(_system.WaitUntil(system => system.Updates > updates, TimeSpan.FromSeconds(1.5)));

        _system.Window.Post(SessionChangeMessage, SessionUnlock);
        Assert.True(_system.WaitUntil(system => system.Updates > updates));
    }

    [Fact]
    public void AFailureEndsTheThreadAndRemovesTheMeters()
    {
        _system.ShowFailure = new InvalidOperationException("The test fails the meters.");
        var thread = Start(AllMeters);

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));

        Assert.True(thread.HasFailed);
        Assert.Contains((LogLevel.Error, "System Meters failed, so it's stopped until PC Companion restarts."), _logger.Entries);
        Assert.Equal("dispose", _system.Events[^1]);
    }

    private static void StopAndJoin(MetersThread thread)
    {
        thread.Stop();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
    }

    private MetersThread Start(SystemMetersSettings settings, MetersThread? previous = null)
    {
        var thread = new MetersThread(settings, _system, _commands, TimeProvider.System, _logger, previous);
        _threads.Add(thread);
        return thread;
    }
}
