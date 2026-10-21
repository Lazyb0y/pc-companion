using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What keeps a program from starting.
/// </summary>
public enum ProgramProblem
{
    None,
    NotChosen,
    NotFullPath,
    NotExe,
    NotFound,
}

/// <summary>
/// The program file that a feature starts, such as a Hot Corners corner's program.
/// </summary>
public static class ProgramFile
{
    /// <summary>
    /// Checks a program path: only the full path of an existing <c>.exe</c> file runs. A name
    /// alone would make Windows search folders for it, and <c>.bat</c> and <c>.cmd</c> files run
    /// through cmd, which reads arguments differently.
    /// </summary>
    public static ProgramProblem Check(string? program, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        if (string.IsNullOrWhiteSpace(program))
        {
            return ProgramProblem.NotChosen;
        }

        if (!Path.IsPathFullyQualified(program))
        {
            return ProgramProblem.NotFullPath;
        }

        if (!string.Equals(Path.GetExtension(program), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return ProgramProblem.NotExe;
        }

        return fileExists(program) ? ProgramProblem.None : ProgramProblem.NotFound;
    }

    /// <summary>
    /// Starts <paramref name="program"/>, a checked full path, in its own folder, with
    /// <paramref name="arguments"/> passed one by one and no shell. Returns false, with Windows'
    /// error code, when Windows couldn't start it; the error's message isn't given, since it
    /// includes the program's folder.
    /// </summary>
    /// <param name="start">Starts a process.</param>
    public static bool TryStart(string program, IEnumerable<string> arguments, Action<ProcessStartInfo> start, out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(start);

        var startInfo = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(program),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            start(startInfo);
            errorCode = 0;
            return true;
        }
        catch (Win32Exception exception)
        {
            errorCode = exception.NativeErrorCode;
            return false;
        }
    }
}
