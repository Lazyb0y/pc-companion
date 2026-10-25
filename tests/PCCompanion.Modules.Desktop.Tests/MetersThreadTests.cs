using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

// The meters thread uses one window class name, so its tests run one at a time.
[CollectionDefinition(nameof(MetersThreads))]
public sealed class MetersThreads;

// Each test runs the meters thread with its own hidden window and a real timer, and fake icons,
// so nothing shows on the taskbar, no menu opens and Task Manager doesn't start. The session's lock
// and the displays' state are fakes too, so the meters run whether or not the PC's screen is on.
[Collection(nameof(MetersThreads))]
public sealed class MetersThreadTests : IDisposable
{
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

        // The meters open no web pages.
        Assert.Empty(_commands.WebPages);
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
    public void StoppingWithTheMenuOpenClosesItAndEndsTheThread()
    {
        _system.MenuStaysOpen = true;
        var thread = Start(AllMeters);
        _system.OnMetersThread(icons => icons.OpenMenu());
        Assert.True(_system.WaitUntil(system => system.Events.Contains("menu open")));

        thread.Stop();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Contains("menu close", _system.Events);
        Assert.Equal("dispose", _system.Events[^1]);
        Assert.False(thread.HasFailed);
    }

    [Fact]
    public void AnIconTheTaskbarTookAgainIsLogged()
    {
        Start(AllMeters);

        _system.OnMetersThread(icons => icons.AddAgain(Meter.Memory));

        Assert.True(_system.WaitUntil(_ => _logger.Entries.Contains((LogLevel.Debug, "The taskbar restarted, and Windows took the Memory meter's icon again."))));
    }

    [Fact]
    public void UpdatesPauseWhileTheSessionIsLocked()
    {
        Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Updates >= 1));

        _system.Pauses!.Lock();
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.StartsWith("System Meters paused: True", StringComparison.Ordinal))));
        var updates = _system.Updates;
        Assert.False(_system.WaitUntil(system => system.Updates > updates, TimeSpan.FromSeconds(1.5)));

        _system.Pauses.Unlock();
        Assert.True(_system.WaitUntil(system => system.Updates > updates));
    }

    [Fact]
    public void UpdatesPauseWhileTheDisplaysAreOff()
    {
        Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Updates >= 1));

        _system.Pauses!.TurnDisplaysOff();
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.StartsWith("System Meters paused: True", StringComparison.Ordinal))));
        var updates = _system.Updates;
        Assert.False(_system.WaitUntil(system => system.Updates > updates, TimeSpan.FromSeconds(1.5)));

        _system.Pauses.TurnDisplaysOn();
        Assert.True(_system.WaitUntil(system => system.Updates > updates));
    }

    [Fact]
    public void TheStripDoesntLookForRoomWhileTheMetersArePaused()
    {
        _system.WindowsBuild = 26100;
        Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("strip show")));

        _system.Pauses!.Lock();
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.StartsWith("System Meters paused: True", StringComparison.Ordinal))));

        // As when a window opens or the strip's 30-second timer fires while the meters wait.
        Assert.Equal(0, TaskbarReadsAfterALayoutEvent());
        Assert.Contains("strip pause", _system.Events);

        var reads = _system.StripHost!.TaskbarReads;
        _system.Pauses.Unlock();

        // The taskbar may have changed while the meters waited, so the strip looks for room once.
        Assert.True(_system.WaitUntil(system => system.Events.Contains("strip resume") && system.StripHost!.TaskbarReads == reads + 1));
        Assert.Equal(1, TaskbarReadsAfterALayoutEvent());
    }

    [Fact]
    public void WhenWindowsRefusesTheStripTheMetersUseIcons()
    {
        _system.WindowsBuild = 26100;
        _system.StripHostFailure = new Win32Exception(5);
        var thread = Start(AllMeters);

        Assert.True(_system.WaitUntil(system => system.Events.Contains("show Cpu")));
        Assert.False(thread.HasFailed);
        Assert.Contains((LogLevel.Warning, "Windows refused the taskbar strip, so the meters use notification icons."), _logger.Entries);
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Contains((LogLevel.Debug, "System Meters show: IconsStripRefused."))));
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

    [Fact]
    public void AFailureInTheStripsOwnHandlersEndsTheThreadAndRemovesTheStrip()
    {
        _system.WindowsBuild = 26100;
        var thread = Start(AllMeters);
        Assert.True(_system.WaitUntil(system => system.Events.Contains("strip show")));

        _system.OnMetersThread(_ => _system.StripGuard!(() => throw new InvalidOperationException("The test fails the strip.")));

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(thread.HasFailed);
        Assert.Contains((LogLevel.Error, "System Meters failed, so it's stopped until PC Companion restarts."), _logger.Entries);
        Assert.True(_system.StripHost!.IsDisposed);
    }

    // Raises the strip host's layout event on the meters thread, and returns how often the strip
    // read the taskbar for it.
    private int TaskbarReadsAfterALayoutEvent()
    {
        var host = _system.StripHost!;
        var reads = -1;
        using var done = new ManualResetEventSlim();
        _system.OnMetersThread(_ =>
        {
            var before = host.TaskbarReads;
            host.RaiseLayoutMayHaveChanged();
            reads = host.TaskbarReads - before;
            done.Set();
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        return reads;
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
