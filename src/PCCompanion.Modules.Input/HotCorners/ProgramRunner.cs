using System;
using System.ComponentModel;
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
public sealed partial class ProgramRunner(ILogger logger, Func<bool> isAdministrator, Func<string, bool> fileExists, Action<ProcessStartInfo> start)
{
    /// <summary>
    /// Starts the corner's program, in its own folder, with its arguments passed one by one and
    /// no shell. Problems are logged, with the program's file name only: its folder and
    /// arguments can hold personal paths or secrets.
    /// </summary>
    public void Run(CornerSettings corner)
    {
        ArgumentNullException.ThrowIfNull(corner);

        if (isAdministrator())
        {
            LogAdministrator(logger);
            return;
        }

        var program = corner.Program;
        var fileName = Path.GetFileName(program) ?? string.Empty;
        var problem = ProgramFile.Check(program, fileExists);
        if (problem != ProgramProblem.None)
        {
            LogNotStarted(logger, fileName, problem);
            return;
        }

        var startInfo = new ProcessStartInfo(program!)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(program),
        };

        foreach (var argument in CommandLine.SplitArguments(corner.Arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            start(startInfo);
            LogStarted(logger, fileName);
        }
        catch (Win32Exception exception)
        {
            // Not logged with the exception, whose message includes the program's folder.
            LogCantStart(logger, fileName, exception.NativeErrorCode);
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
}
