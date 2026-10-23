using System;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Starts the program of a corner whose action is Run a program. See "Run a program" in
/// docs/features/input/hot-corners.md.
/// </summary>
/// <param name="fileExists">Whether a file exists.</param>
/// <param name="commands">The host's commands, which start the program without admin rights.</param>
/// <param name="runInBackground">Runs an action on the thread pool.</param>
public sealed partial class ProgramRunner(ILogger logger, Func<string, bool> fileExists, IHostCommands commands, Action<Action> runInBackground)
{
    /// <summary>
    /// Starts the corner's program with the user's rights, also while PC Companion runs as
    /// administrator, in its own folder, with its arguments passed one by one and no shell. The
    /// program is checked and started on the thread pool, and this returns at once: a program on
    /// a network share that's offline can take many seconds. Problems are logged, with the
    /// program's file name only: its folder and arguments can hold personal paths or secrets.
    /// <paramref name="done"/> gets the result on the thread pool.
    /// </summary>
    public void Run(CornerSettings corner, Action<CornerResultKind> done)
    {
        ArgumentNullException.ThrowIfNull(corner);
        ArgumentNullException.ThrowIfNull(done);

        runInBackground(() => done(RunGuarded(corner)));
    }

    // On the thread pool, where an exception wouldn't reach the feature's error boundary and
    // would end the host.
    private CornerResultKind RunGuarded(CornerSettings corner)
    {
        var fileName = Path.GetFileName(corner.Program) ?? string.Empty;
        try
        {
            return Start(corner, fileName);
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, fileName);
            return CornerResultKind.ProgramCantStart;
        }
    }

    private CornerResultKind Start(CornerSettings corner, string fileName)
    {
        var program = corner.Program;
        var problem = ProgramFile.Check(program, fileExists);
        if (problem != ProgramProblem.None)
        {
            LogNotStarted(logger, fileName, problem);
            return problem switch
            {
                ProgramProblem.NotChosen => CornerResultKind.ProgramNotChosen,
                ProgramProblem.NotFound => CornerResultKind.ProgramNotFound,
                _ => CornerResultKind.ProgramNotExe,
            };
        }

        if (commands.StartProgram(program!, CommandLine.SplitArguments(corner.Arguments), out var errorCode))
        {
            LogStarted(logger, fileName);
            return CornerResultKind.Ran;
        }

        LogCantStart(logger, fileName, errorCode);
        return CornerResultKind.ProgramCantStart;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started {FileName}.")]
    private static partial void LogStarted(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run a program didn't start {FileName}: {Problem}.")]
    private static partial void LogNotStarted(ILogger logger, string fileName, ProgramProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows couldn't start {FileName} (error {ErrorCode}).")]
    private static partial void LogCantStart(ILogger logger, string fileName, int errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run a program failed to start {FileName}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string fileName);
}
