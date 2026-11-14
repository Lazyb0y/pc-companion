using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A program this process started and watches: <see cref="Exited"/> completes with its exit code
/// when it ends. Dispose it once it ended.
/// </summary>
public sealed class RunningProcess : IDisposable
{
    private readonly IDisposable _owner;

    private RunningProcess(int id, Task<int> exited, IDisposable owner)
    {
        Id = id;
        Exited = exited;
        _owner = owner;
    }

    public int Id { get; }

    /// <summary>Completes with the program's exit code when it ends.</summary>
    public Task<int> Exited { get; }

    /// <summary>
    /// Starts the program at <paramref name="path"/> with this process's rights, in its own folder,
    /// with its arguments passed one by one and no shell.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused to start it.</exception>
    public static RunningProcess Start(string path, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException("The program didn't start.");
        return new RunningProcess(process.Id, ExitOf(process), process);

        static async Task<int> ExitOf(Process process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    /// <summary>A program watched by other means, whose end <paramref name="exited"/> reports, as in tests.</summary>
    public static RunningProcess Watch(int id, Task<int> exited) => new(id, exited, new NothingToDispose());

    public void Dispose() => _owner.Dispose();

    /// <summary>A program started with a process handle of its own, as <see cref="UnelevatedProcess.StartKept"/> does.</summary>
    internal static RunningProcess FromHandle(SafeProcessHandle handle, int id) => new(id, ExitOf(handle), handle);

    private static async Task<int> ExitOf(SafeProcessHandle handle)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var wait = new ProcessWaitHandle(handle))
        {
            var registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) => ended.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
            try
            {
                await ended.Task.ConfigureAwait(false);
            }
            finally
            {
                registration.Unregister(null);
            }
        }

        return ExitCode(handle);
    }

    private static unsafe int ExitCode(SafeProcessHandle handle)
    {
        uint code;
        return PInvoke.GetExitCodeProcess((HANDLE)handle.DangerousGetHandle(), &code) ? unchecked((int)code) : -1;
    }

    private sealed class NothingToDispose : IDisposable
    {
        public void Dispose()
        {
        }
    }

    // Waits on the process handle, which stays the caller's.
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process)
        {
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
        }
    }
}
