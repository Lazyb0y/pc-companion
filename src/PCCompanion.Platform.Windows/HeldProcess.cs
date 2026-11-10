using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Another process, held open with the rights to end it, wait for it and read its basic details,
/// from the moment the user chooses it until it ends. While it's held, Windows doesn't give its ID
/// to another process, so ending it can't hit the wrong one.
/// </summary>
public sealed class HeldProcess : IDisposable
{
    private const PROCESS_ACCESS_RIGHTS Rights =
        PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION;

    // The exit code Task Manager gives the processes it ends.
    private const uint EndedExitCode = 1;

    private readonly SafeHandle _process;

    private HeldProcess(SafeHandle process, uint id)
    {
        _process = process;
        Id = id;
        StartTime = ProcessList.StartTime(process);
        ProgramPath = AppIdentity.ProgramPath(process);
    }

    /// <summary>The process's ID.</summary>
    public uint Id { get; }

    /// <summary>When the process started, in UTC, or <see langword="null"/> when it can't be read.</summary>
    public DateTime? StartTime { get; }

    /// <summary>The full path of the process's program file, or <see langword="null"/> when it can't be read.</summary>
    public string? ProgramPath { get; }

    /// <summary>
    /// Whether Windows marks the process critical, so that ending it stops Windows; <see langword="null"/>
    /// when that can't be read.
    /// </summary>
    public bool? IsCritical => PInvoke.IsProcessCritical(_process, out var critical) ? critical : null;

    /// <summary>Whether the process has ended.</summary>
    public bool HasExited => PInvoke.WaitForSingleObject(_process, 0) == WAIT_EVENT.WAIT_OBJECT_0;

    /// <summary>
    /// Opens the process with ID <paramref name="processId"/>. Returns <see langword="null"/>, with
    /// Windows' error code, when Windows refuses, as for processes of apps running as administrator
    /// while this process doesn't, other users' and protected processes, or when it has ended.
    /// </summary>
    public static HeldProcess? Open(uint processId, out int errorCode)
    {
        var process = PInvoke.OpenProcess_SafeHandle(Rights, false, processId);
        if (process.IsInvalid)
        {
            errorCode = Marshal.GetLastPInvokeError();
            process.Dispose();
            return null;
        }

        errorCode = 0;
        return new HeldProcess(process, processId);
    }

    /// <summary>
    /// Asks Windows to end the process at once, without asking it first, as Task Manager's End task
    /// does. Windows ends it asynchronously; see <see cref="WaitForExit"/>. Returns false, with
    /// Windows' error code, when Windows refused.
    /// </summary>
    public bool End(out int errorCode)
    {
        if (PInvoke.TerminateProcess(_process, EndedExitCode))
        {
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastPInvokeError();
        return false;
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the process to end. Returns whether it has. It
    /// blocks, so don't call it on a thread that handles windows' messages.
    /// </summary>
    public bool WaitForExit(TimeSpan timeout) =>
        PInvoke.WaitForSingleObject(_process, (uint)Math.Clamp(timeout.TotalMilliseconds, 0, uint.MaxValue - 1)) == WAIT_EVENT.WAIT_OBJECT_0;

    public void Dispose() => _process.Dispose();
}
