using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Starts the program of a corner whose action is Run a program. See "Run a program" in
/// docs/features/input/hot-corners.md.
/// </summary>
/// <param name="isAdministrator">Whether PC Companion runs as administrator; programs don't start then.</param>
/// <param name="fileExists">Whether a file exists.</param>
/// <param name="start">Starts a process.</param>
/// <param name="runInBackground">Runs an action on the thread pool.</param>
public sealed partial class ProgramRunner(ILogger logger, Func<bool> isAdministrator, Func<string, bool> fileExists, Action<ProcessStartInfo> start, Action<Action> runInBackground)
{
    /// <summary>
    /// Starts the corner's program, in its own folder, with its arguments passed one by one and
    /// no shell. The program is checked and started on the thread pool, and this returns at once:
    /// a program on a network share that's offline can take many seconds. Problems are logged,
    /// with the program's file name only: its folder and arguments can hold personal paths or
    /// secrets.
    /// </summary>
    public void Run(CornerSettings corner)
    {
        ArgumentNullException.ThrowIfNull(corner);

        runInBackground(() => RunGuarded(corner));
    }

    // On the thread pool, where an exception wouldn't reach the feature's error boundary and
    // would end the host.
    private void RunGuarded(CornerSettings corner)
    {
        var fileName = Path.GetFileName(corner.Program) ?? string.Empty;
        try
        {
            Start(corner, fileName);
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, fileName);
        }
    }

    private void Start(CornerSettings corner, string fileName)
    {
        if (isAdministrator())
        {
            LogAdministrator(logger);
            return;
        }

        var program = corner.Program;
        var problem = ProgramFile.Check(program, fileExists);
        if (problem != ProgramProblem.None)
        {
            LogNotStarted(logger, fileName, problem);
            return;
        }

        if (ProgramFile.TryStart(program!, CommandLine.SplitArguments(corner.Arguments), start, out var errorCode))
        {
            LogStarted(logger, fileName);
        }
        else
        {
            LogCantStart(logger, fileName, errorCode);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started {FileName}.")]
    private static partial void LogStarted(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run a program doesn't run while PC Companion runs as administrator.")]
    private static partial void LogAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run a program didn't start {FileName}: {Problem}.")]
    private static partial void LogNotStarted(ILogger logger, string fileName, ProgramProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows couldn't start {FileName} (error {ErrorCode}).")]
    private static partial void LogCantStart(ILogger logger, string fileName, int errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run a program failed to start {FileName}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string fileName);
}
