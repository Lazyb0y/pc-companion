using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Features;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class HostCommandsTests
{
    private static readonly Uri Address = new("https://tracking.example.com/?number=1Z999");

    private readonly RecordingLogger<HostCommands> _logger = new();

    // Stands in for the host's UI thread: actions wait until the test runs them.
    private readonly ConcurrentQueue<Action> _uiThread = new();

    [Fact]
    public async Task CommandsAskedForOnAnotherThreadRunOnTheUiThread()
    {
        var commands = new HostCommands(_logger);
        int settings = 0, exits = 0;
        commands.Attach(_uiThread.Enqueue, () => settings++, () => exits++, _ => true);

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
        Assert.False(commands.OpenWebPage(Address));

        Assert.Empty(_uiThread);
        Assert.Equal(3, _logger.Entries.FindAll(entry => entry.Level == LogLevel.Warning).Count);
    }

    [Fact]
    public void AFailingCommandIsLoggedAndDoesNotReachTheUiThreadsLoop()
    {
        var commands = new HostCommands(_logger);
        commands.Attach(_uiThread.Enqueue, () => throw new InvalidOperationException("Can't open."), () => { }, _ => true);

        commands.OpenSettings();
        RunUiThread();

        Assert.Contains(_logger.Entries, entry => entry is { Level: LogLevel.Error, Message: "Settings, which a feature asked for, failed." });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWebPageOpensOnTheCallingThreadWithTheHostsAnswer(bool opened)
    {
        var commands = new HostCommands(_logger);
        var asked = new List<Uri>();
        commands.Attach(_uiThread.Enqueue, () => { }, () => { }, address =>
        {
            asked.Add(address);
            return opened;
        });

        Assert.Equal(opened, commands.OpenWebPage(Address));

        Assert.Equal([Address], asked);
        Assert.Empty(_uiThread);
    }

    [Fact]
    public void AWebPageIsLoggedWithoutItsAddress()
    {
        var commands = new HostCommands(_logger);
        commands.Attach(_uiThread.Enqueue, () => { }, () => { }, _ => true);

        commands.OpenWebPage(Address);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Debug, "A feature asked for a web page."), (entry.Level, entry.Message));
    }

    private void RunUiThread()
    {
        while (_uiThread.TryDequeue(out var action))
        {
            action();
        }
    }
}
