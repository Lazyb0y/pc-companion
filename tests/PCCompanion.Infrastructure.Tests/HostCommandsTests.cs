using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class HostCommandsTests
{
    private static readonly Uri Address = new("https://tracking.example.com/?number=1Z999");
    private const string Program = @"C:\Users\Someone\Tools\Viewer.exe";

    private readonly RecordingLogger<HostCommands> _logger = new();

    // Stands in for the host's UI thread: actions wait until the test runs them.
    private readonly ConcurrentQueue<Action> _uiThread = new();

    [Fact]
    public async Task CommandsAskedForOnAnotherThreadRunOnTheUiThread()
    {
        var commands = new HostCommands(_logger);
        int settings = 0, exits = 0;
        commands.Attach(_uiThread.Enqueue, _ => settings++, () => exits++, _ => true, (_, _) => 0);

        await Task.Run(commands.OpenSettings, TestContext.Current.CancellationToken);
        await Task.Run(commands.ExitApp, TestContext.Current.CancellationToken);

        Assert.Equal((0, 0), (settings, exits));
        RunUiThread();
        Assert.Equal((1, 1), (settings, exits));
    }

    [Fact]
    public void AFeaturesSettingsOpenOnItsPage()
    {
        var commands = new HostCommands(_logger);
        var pages = new List<string?>();
        commands.Attach(_uiThread.Enqueue, pages.Add, () => { }, _ => true, (_, _) => 0);

        commands.OpenSettings();
        commands.OpenFeatureSettings("networkProfiles");
        RunUiThread();

        Assert.Equal([null, "networkProfiles"], pages);
    }

    [Fact]
    public void ACommandBeforeTheHostIsReadyIsDropped()
    {
        var commands = new HostCommands(_logger);

        commands.OpenSettings();
        commands.ExitApp();
        Assert.False(commands.OpenWebPage(Address));
        Assert.False(commands.StartProgram(Program, [], out var errorCode));

        Assert.Equal(21, errorCode);
        Assert.Empty(_uiThread);
        Assert.Equal(4, _logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public void AFailingCommandIsLoggedAndDoesNotReachTheUiThreadsLoop()
    {
        var commands = new HostCommands(_logger);
        commands.Attach(_uiThread.Enqueue, _ => throw new InvalidOperationException("Can't open."), () => { }, _ => true, (_, _) => 0);

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
        commands.Attach(_uiThread.Enqueue, _ => { }, () => { }, address =>
        {
            asked.Add(address);
            return opened;
        }, (_, _) => 0);

        Assert.Equal(opened, commands.OpenWebPage(Address));

        Assert.Equal([Address], asked);
        Assert.Empty(_uiThread);
    }

    [Fact]
    public void AWebPageIsLoggedWithoutItsAddress()
    {
        var commands = new HostCommands(_logger);
        commands.Attach(_uiThread.Enqueue, _ => { }, () => { }, _ => true, (_, _) => 0);

        commands.OpenWebPage(Address);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Debug, "A feature asked for a web page."), (entry.Level, entry.Message));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(740)] // ERROR_ELEVATION_REQUIRED
    public void AProgramStartsOnTheCallingThreadWithTheHostsAnswer(int answer)
    {
        var commands = new HostCommands(_logger);
        var asked = new List<(string, IReadOnlyList<string>)>();
        commands.Attach(_uiThread.Enqueue, _ => { }, () => { }, _ => true, (program, arguments) =>
        {
            asked.Add((program, arguments));
            return answer;
        });

        Assert.Equal(answer == 0, commands.StartProgram(Program, ["--open", "a b"], out var errorCode));

        Assert.Equal(answer, errorCode);
        var (program, arguments) = Assert.Single(asked);
        Assert.Equal(Program, program);
        Assert.Equal(["--open", "a b"], arguments);
        Assert.Empty(_uiThread);
    }

    [Fact]
    public void AProgramIsLoggedWithoutItsPathOrArguments()
    {
        var commands = new HostCommands(_logger);
        commands.Attach(_uiThread.Enqueue, _ => { }, () => { }, _ => true, (_, _) => 0);

        commands.StartProgram(Program, ["copied text"], out _);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Debug, "A feature asked for a program."), (entry.Level, entry.Message));
    }

    private void RunUiThread()
    {
        while (_uiThread.TryDequeue(out var action))
        {
            action();
        }
    }
}
