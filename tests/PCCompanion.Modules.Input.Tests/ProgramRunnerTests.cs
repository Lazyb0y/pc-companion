using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// A fake starter records what would start; no program runs. The work meant for the thread pool
// waits in a list until the test runs it.
public sealed class ProgramRunnerTests
{
    private const string Program = @"C:\Program Files\Some App\App.exe";
    private const string OtherProgram = @"C:\Windows\System32\charmap.exe";

    private readonly RecordingLogger<HotCornersFeature> _logger = new();
    private readonly List<ProcessStartInfo> _started = [];
    private readonly List<string> _checked = [];
    private readonly List<Action> _background = [];
    private readonly ProgramRunner _runner;
    private bool _isAdministrator;
    private int _startError;
    private Exception? _startFailure;

    public ProgramRunnerTests()
    {
        _runner = new ProgramRunner(_logger, () => _isAdministrator, Exists, Start, _background.Add);
    }

    [Fact]
    public void TheProgramStartsInItsFolderWithItsArgumentsAndNoShell()
    {
        Run(new CornerSettings { Program = Program, Arguments = "--new \"two words\"" });

        var startInfo = Assert.Single(_started);
        Assert.Equal(Program, startInfo.FileName);
        Assert.Equal(["--new", "two words"], startInfo.ArgumentList);
        Assert.Equal(@"C:\Program Files\Some App", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal([(LogLevel.Debug, "Started App.exe.")], _logger.Entries);
    }

    [Fact]
    public void RunReturnsBeforeTheProgramIsCheckedOrStarted()
    {
        _runner.Run(new CornerSettings { Action = HotCornerAction.RunProgram, Program = Program });

        Assert.Single(_background);
        Assert.Empty(_checked);
        Assert.Empty(_started);
        Assert.Empty(_logger.Entries);

        RunBackground();

        Assert.Equal([Program], _checked);
        Assert.Equal(Program, Assert.Single(_started).FileName);
    }

    [Fact]
    public void TwoCornersRightAfterEachOtherStartTheirOwnPrograms()
    {
        _runner.Run(new CornerSettings { Action = HotCornerAction.RunProgram, Program = Program, Arguments = "--first" });
        _runner.Run(new CornerSettings { Action = HotCornerAction.RunProgram, Program = OtherProgram, Arguments = "--second" });

        // The thread pool can run them in either order.
        Assert.Equal(2, _background.Count);
        _background[1]();
        _background[0]();

        Assert.Equal([(OtherProgram, "--second"), (Program, "--first")], _started.Select(startInfo => (startInfo.FileName, string.Join(' ', startInfo.ArgumentList))));
    }

    [Fact]
    public void NothingStartsWhilePCCompanionRunsAsAdministrator()
    {
        _isAdministrator = true;

        Run(new CornerSettings { Program = Program });

        Assert.Empty(_started);
        Assert.Equal([(LogLevel.Warning, "Run a program doesn't run while PC Companion runs as administrator.")], _logger.Entries);
    }

    [Fact]
    public void AProgramThatCantRunIsLoggedByItsFileNameOnly()
    {
        Run(new CornerSettings { Program = @"C:\Users\Someone\Scripts\Clean.bat", Arguments = "--secret 123" });

        Assert.Empty(_started);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "Run a program didn't start Clean.bat: NotExe."), entry);
    }

    [Fact]
    public void AStartThatWindowsRefusesIsLoggedWithItsErrorCode()
    {
        _startError = 740; // ERROR_ELEVATION_REQUIRED

        Run(new CornerSettings { Program = Program, Arguments = "--secret 123" });

        Assert.Equal([(LogLevel.Warning, "Windows couldn't start App.exe (error 740).")], _logger.Entries);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("Program Files", StringComparison.Ordinal) || entry.Message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnexpectedFailureIsLoggedAndGoesNoFurther()
    {
        _startFailure = new InvalidOperationException("Something went wrong.");

        Run(new CornerSettings { Program = Program, Arguments = "--secret 123" });

        Assert.Empty(_started);
        Assert.Equal([(LogLevel.Error, "Run a program failed to start App.exe.")], _logger.Entries);
    }

    private bool Exists(string path)
    {
        _checked.Add(path);
        return new[] { Program, OtherProgram, @"C:\Scripts\Clean.bat", @"C:\Scripts\Clean.cmd", @"C:\Users\Public\Desktop\App.lnk" }
            .Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    private void Start(ProcessStartInfo startInfo)
    {
        if (_startError != 0)
        {
            throw new Win32Exception(_startError, $"An error occurred trying to start process '{startInfo.FileName}'.");
        }

        if (_startFailure is { } failure)
        {
            throw failure;
        }

        _started.Add(startInfo);
    }

    // Runs the corner's program, and then the work it left for the thread pool.
    private void Run(CornerSettings corner)
    {
        _runner.Run(corner with { Action = HotCornerAction.RunProgram });
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
