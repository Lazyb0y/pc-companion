using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Features;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class HostCommandsTests
{
    private readonly RecordingLogger<HostCommands> _logger = new();

    // Stands in for the host's UI thread: actions wait until the test runs them.
    private readonly ConcurrentQueue<Action> _uiThread = new();

    [Fact]
    public async Task CommandsAskedForOnAnotherThreadRunOnTheUiThread()
    {
        var commands = new HostCommands(_logger);
        int settings = 0, exits = 0;
        commands.Attach(_uiThread.Enqueue, () => settings++, () => exits++);

        await Task.Run(commands.OpenSettings, TestContext.Current.CancellationToken);
        await Task.Run(commands.ExitApp, TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (settings, exits));
        RunUiThread();
        Assert.Equal((1, 1), (settings, exits));
    }

    [Fact]
    public void ACommandBeforeTheHostIsReadyIsDropped()
    {
        var commands = new HostCommands(_logger);

        commands.OpenSettings();
        commands.ExitApp();

        Assert.Empty(_uiThread);
        Assert.Equal(2, _logger.Entries.FindAll(entry => entry.Level == LogLevel.Warning).Count);
    }

    [Fact]
    public void AFailingCommandIsLoggedAndDoesNotReachTheUiThreadsLoop()
    {
        var commands = new HostCommands(_logger);
        commands.Attach(_uiThread.Enqueue, () => throw new InvalidOperationException("Can't open."), () => { });

        commands.OpenSettings();
        RunUiThread();

        Assert.Contains(_logger.Entries, entry => entry is { Level: LogLevel.Error, Message: "Settings, which a feature asked for, failed." });
    }

    private void RunUiThread()
    {
        while (_uiThread.TryDequeue(out var action))
        {
            action();
        }
    }
}
