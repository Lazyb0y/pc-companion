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

// A fake starter records what would start; no program runs.
public sealed class ProgramRunnerTests
{
    private const string Program = @"C:\Program Files\Some App\App.exe";

    private readonly RecordingLogger<HotCornersFeature> _logger = new();
    private readonly List<ProcessStartInfo> _started = [];
    private bool _isAdministrator;
    private int _startError;

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

    private static bool Exists(string path) =>
        new[] { Program, @"C:\Scripts\Clean.bat", @"C:\Scripts\Clean.cmd", @"C:\Users\Public\Desktop\App.lnk" }
            .Contains(path, StringComparer.OrdinalIgnoreCase);

    private void Run(CornerSettings corner)
    {
        var runner = new ProgramRunner(_logger, () => _isAdministrator, Exists, startInfo =>
        {
            if (_startError != 0)
            {
                throw new Win32Exception(_startError, $"An error occurred trying to start process '{startInfo.FileName}'.");
            }

            _started.Add(startInfo);
        });

        runner.Run(corner with { Action = HotCornerAction.RunProgram });
    }
}
