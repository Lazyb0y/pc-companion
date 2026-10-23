using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Starts the programs features ask for, such as a Hot Corners corner's, never with admin rights:
/// any program running as the user can change the settings file that names them. A host running as
/// administrator through UAC starts a copy of itself with <see cref="HostProcess.RunProgramArgument"/>
/// without admin rights and hands it the program and its arguments through a one-time pipe, since
/// Windows starts that copy with a command line of 1,024 characters at most; the copy starts the
/// program. With UAC off, every program of the user's runs with admin rights anyway. See
/// docs/features/general/run-as-administrator.md.
/// </summary>
internal static class ProgramCommand
{
    private const string PipeName = "PCCompanion.RunProgram";
    private const int FileNotFound = 2; // ERROR_FILE_NOT_FOUND
    private const int AccessDenied = 5; // ERROR_ACCESS_DENIED
    private const int InvalidData = 13; // ERROR_INVALID_DATA
    private const int Timeout = 1460; // ERROR_TIMEOUT

    private static readonly TimeSpan CopyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts <paramref name="program"/>, a checked full path, from the running host, on the calling
    /// thread. Returns 0, or Windows' error code when it wasn't started.
    /// </summary>
    public static int Start(string program, IReadOnlyList<string> arguments)
    {
        if (!Elevation.IsUacElevated)
        {
            return ProgramFile.TryStart(program, arguments, StartProcess, out var errorCode) ? 0 : errorCode;
        }

        // A name of its own each time, so the copy reads this hand-off and no other.
        var pipeName = SnapshotPipe.ForSession(PipeName) + "." + Guid.NewGuid().ToString("N");
        var handOff = new ProgramToStart(program, [.. arguments]).ToJson();
        try
        {
            using var pipe = SnapshotPipe.Serve(pipeName, () => handOff, _ => { });
            return UnelevatedProcess.Run(HostProcess.ExecutablePath, HostProcess.RunProgramArguments(pipeName), CopyTimeout) is { } exitCode
                ? (int)exitCode
                : Timeout;
        }
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode;
        }
    }

    /// <summary>Runs the copy started with <see cref="HostProcess.RunProgramArgument"/>, and returns its exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        // Started with admin rights, for example by hand, it starts nothing.
        if (Elevation.IsUacElevated)
        {
            return AccessDenied;
        }

        if (HostProcess.ReadRunProgramPipe(arguments) is not { } pipeName
            || SnapshotPipe.ReadAsync(pipeName, PipeTimeout, ProgramToStart.MaximumLength).GetAwaiter().GetResult() is not { } handOff
            || ProgramToStart.FromJson(handOff) is not { } program)
        {
            return InvalidData;
        }

        // The running host checked the program, and it's checked again here, since only a full path
        // to an .exe file starts.
        if (ProgramFile.Check(program.Program, File.Exists) != ProgramProblem.None)
        {
            return FileNotFound;
        }

        return ProgramFile.TryStart(program.Program, program.Arguments, StartProcess, out var errorCode) ? 0 : errorCode;
    }

    private static void StartProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
    }
}
