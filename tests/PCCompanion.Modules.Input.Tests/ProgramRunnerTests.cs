using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// The host's commands are a fake that records what would start; no program runs. The work meant
// for the thread pool waits in a list until the test runs it.
public sealed class ProgramRunnerTests
{
    private const string Program = @"C:\Program Files\Some App\App.exe";
    private const string OtherProgram = @"C:\Windows\System32\charmap.exe";

    private readonly RecordingLogger<HotCornersFeature> _logger = new();
    private readonly FakeHostCommands _commands = new();
    private readonly List<string> _checked = [];
    private readonly List<Action> _background = [];
    private readonly List<CornerResultKind> _done = [];
    private readonly ProgramRunner _runner;

    public ProgramRunnerTests()
    {
        _runner = new ProgramRunner(_logger, Exists, _commands, _background.Add);
    }

    [Fact]
    public void TheHostStartsTheProgramWithItsArgumentsOneByOne()
    {
        Run(new CornerSettings { Program = Program, Arguments = "--new \"two words\"" });

        var (program, arguments) = Assert.Single(_commands.Programs);
        Assert.Equal(Program, program);
        Assert.Equal(["--new", "two words"], arguments);
        Assert.Equal([(LogLevel.Debug, "Started App.exe.")], _logger.Entries);
        Assert.Equal([CornerResultKind.Ran], _done);
    }

    [Theory]
    [InlineData(null, CornerResultKind.ProgramNotChosen)]
    [InlineData(@"C:\Missing\App.exe", CornerResultKind.ProgramNotFound)]
    [InlineData(@"C:\Scripts\Clean.cmd", CornerResultKind.ProgramNotExe)]
    [InlineData("App.exe", CornerResultKind.ProgramNotExe)]
    public void AProgramThatCantRunGivesWhy(string? program, CornerResultKind result)
    {
        Run(new CornerSettings { Program = program });

        Assert.Equal([result], _done);
        Assert.Empty(_commands.Programs);
    }

    [Fact]
    public void RunReturnsBeforeTheProgramIsCheckedOrStarted()
    {
        _runner.Run(new CornerSettings { Action = HotCornerAction.RunProgram, Program = Program }, _done.Add);

        Assert.Single(_background);
        Assert.Empty(_checked);
        Assert.Empty(_commands.Programs);
        Assert.Empty(_logger.Entries);

        RunBackground();

        Assert.Equal([Program], _checked);
        Assert.Equal(Program, Assert.Single(_commands.Programs).Program);
    }

    [Fact]
    public void TwoCornersRightAfterEachOtherStartTheirOwnPrograms()
    {
        _runner.Run(new CornerSettings { Action = HotCornerAction.RunProgram, Program = Program, Arguments = "--first" }, _done.Add);
        _runner.Run(new CornerSettings { Action = HotCornerAction.RunProgram, Program = OtherProgram, Arguments = "--second" }, _done.Add);

        // The thread pool can run them in either order.
        Assert.Equal(2, _background.Count);
        _background[1]();
        _background[0]();

        Assert.Equal([(OtherProgram, "--second"), (Program, "--first")], _commands.Programs.Select(started => (started.Program, string.Join(' ', started.Arguments))));
    }

    [Fact]
    public void AProgramThatCantRunIsLoggedByItsFileNameOnly()
    {
        Run(new CornerSettings { Program = @"C:\Users\Someone\Scripts\Clean.bat", Arguments = "--secret 123" });

        Assert.Empty(_commands.Programs);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "Run a program didn't start Clean.bat: NotExe."), entry);
    }

    [Fact]
    public void AStartThatWindowsRefusesIsLoggedWithItsErrorCode()
    {
        _commands.ProgramErrorCode = 740; // ERROR_ELEVATION_REQUIRED

        Run(new CornerSettings { Program = Program, Arguments = "--secret 123" });

        Assert.Equal([(LogLevel.Warning, "Windows couldn't start App.exe (error 740).")], _logger.Entries);
        Assert.Equal([CornerResultKind.ProgramCantStart], _done);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Program Files", StringComparison.Ordinal) || entry.Message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnexpectedFailureIsLoggedAndGoesNoFurther()
    {
        _commands.ProgramFailure = new InvalidOperationException("Something went wrong.");

        Run(new CornerSettings { Program = Program, Arguments = "--secret 123" });

        Assert.Equal([(LogLevel.Error, "Run a program failed to start App.exe.")], _logger.Entries);
        Assert.Equal([CornerResultKind.ProgramCantStart], _done);
    }

    private bool Exists(string path)
    {
        _checked.Add(path);
        return new[] { Program, OtherProgram, @"C:\Scripts\Clean.bat", @"C:\Scripts\Clean.cmd", @"C:\Users\Public\Desktop\App.lnk" }
            .Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    // Runs the corner's program, and then the work it left for the thread pool.
    private void Run(CornerSettings corner)
    {
        _runner.Run(corner with { Action = HotCornerAction.RunProgram }, _done.Add);
        RunBackground();
    }

    private void RunBackground()
    {
        foreach (var action in _background)
        {
            action();
        }

        _background.Clear();
    }
}
