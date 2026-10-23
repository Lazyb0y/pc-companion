using System;
using System.ComponentModel;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Hands work, such as starting a program for a feature, from a host running as administrator
/// through UAC to a copy of itself without admin rights. The copy starts with the token of the
/// Windows shell and the name of a one-time pipe, for the user's account and the system only,
/// through which it reads what to do, since Windows starts it with a command line of 1,024
/// characters at most. See docs/features/general/run-as-administrator.md.
/// </summary>
internal static class UnelevatedCopy
{
    private const int Timeout = 1460; // ERROR_TIMEOUT

    private static readonly TimeSpan CopyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts a copy with the arguments <paramref name="arguments"/> makes from the pipe's name,
    /// hands it <paramref name="handOff"/> through a pipe whose name starts with
    /// <paramref name="pipeName"/>, and waits up to 30 seconds for it to exit, on the calling
    /// thread. Returns the copy's exit code, or Windows' error code when it didn't start or exit.
    /// </summary>
    public static int Run(string pipeName, byte[] handOff, Func<string, string> arguments)
    {
        // A name of its own each time, so the copy reads this hand-off and no other.
        var name = SnapshotPipe.ForSession(pipeName) + "." + Guid.NewGuid().ToString("N");
        try
        {
            using var pipe = SnapshotPipe.Serve(name, () => handOff, _ => { });
            return UnelevatedProcess.Run(HostProcess.ExecutablePath, arguments(name), CopyTimeout) is { } exitCode
                ? (int)exitCode
                : Timeout;
        }
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode;
        }
    }

    /// <summary>
    /// In the copy: what the host handed it through the pipe named <paramref name="pipeName"/>, or
    /// <see langword="null"/> when there's no name or no such pipe, or the hand-off doesn't arrive
    /// within 10 seconds or is longer than <paramref name="maximumLength"/>.
    /// </summary>
    public static byte[]? Read(string? pipeName, int maximumLength) =>
        pipeName is null ? null : SnapshotPipe.ReadAsync(pipeName, PipeTimeout, maximumLength).GetAwaiter().GetResult();
}
