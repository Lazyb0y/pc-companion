using System;
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
}
