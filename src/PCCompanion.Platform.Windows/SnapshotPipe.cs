using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A named pipe through which this process gives the other programs of the user's account on this
/// PC a snapshot of something only it knows, such as Hot Corners' recent results. It only writes:
/// a program that connects gets the snapshot and the pipe closes, and nothing it could send is
/// read. Programs on other PCs are rejected. It waits for programs on the thread pool.
/// </summary>
public sealed class SnapshotPipe : IDisposable
{
    private const int BufferSize = 64 * 1024;

    private readonly string _name;
    private readonly Func<byte[]> _snapshot;
    private readonly Action<Exception> _failed;
    private readonly CancellationTokenSource _stop = new();
    private NamedPipeServerStream? _pipe;

    private SnapshotPipe(string name, Func<byte[]> snapshot, Action<Exception> failed, NamedPipeServerStream pipe)
    {
        _name = name;
        _snapshot = snapshot;
        _failed = failed;
        _pipe = pipe;
        _ = Task.Run(ServeAsync);
    }

    /// <summary>
    /// The name of a pipe for this user session, such as <c>PCCompanion.HotCorners.Results.1</c>:
    /// pipe names are shared by every session on the PC.
    /// </summary>
    public static string ForSession(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        using var process = Process.GetCurrentProcess();
        return $"{name}.{process.SessionId}";
    }

    /// <summary>
    /// Gives each program that connects to the pipe named <paramref name="name"/> what
    /// <paramref name="snapshot"/> returns then, until disposed. <paramref name="snapshot"/> runs
    /// on the thread pool, and so does <paramref name="failed"/> when the pipe stops working.
    /// </summary>
    /// <exception cref="Win32Exception">Windows couldn't create the pipe.</exception>
    public static SnapshotPipe Serve(string name, Func<byte[]> snapshot, Action<Exception> failed)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(failed);

        return new SnapshotPipe(name, snapshot, failed, Create(name));
    }

    /// <summary>
    /// Reads the snapshot of the pipe named <paramref name="name"/>, or returns
    /// <see langword="null"/> when no program serves it within <paramref name="timeout"/>, it
    /// isn't given within that time, or it's longer than <paramref name="maximumLength"/>.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(string name, TimeSpan timeout, int maximumLength, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeLimit.Token).ConfigureAwait(false);

            using var snapshot = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await pipe.ReadAsync(buffer, timeLimit.Token).ConfigureAwait(false)) > 0)
            {
                if (snapshot.Length + read > maximumLength)
                {
                    return null;
                }

                snapshot.Write(buffer, 0, read);
            }

            return snapshot.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Stops serving. A program reading a snapshot at that moment may get none.</summary>
    public void Dispose()
    {
        _stop.Cancel();

        // Closing the pipe ends its wait for a program at once.
        Interlocked.Exchange(ref _pipe, null)?.Dispose();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (Volatile.Read(ref _pipe) is { } pipe)
            {
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    await pipe.WriteAsync(_snapshot(), _stop.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The program left before reading.
                }

                // Closing the pipe, rather than disconnecting it, leaves what's written for the
                // program to read. A pipe of the same name waits for the next program.
                var next = Create(_name);
                if (Interlocked.CompareExchange(ref _pipe, next, pipe) == pipe)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await next.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _failed(exception);
        }
    }

    // Outbound only, so the pipe takes no input. Only the user's account and the system may open
    // it, whatever rights this process has: an elevated process's default owner is the
    // Administrators group, which a program without admin rights can't use.
    private static unsafe NamedPipeServerStream Create(string name)
    {
        string user;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            user = identity.User!.Value;
        }

        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;{user})(A;;GA;;;SY)");
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);

        fixed (byte* securityDescriptor = binary)
        {
            var attributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = securityDescriptor,
            };

            using var handle = PInvoke.CreateNamedPipe(
                @"\\.\pipe\" + name,
                FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_OUTBOUND | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
                NAMED_PIPE_MODE.PIPE_TYPE_BYTE | NAMED_PIPE_MODE.PIPE_READMODE_BYTE | NAMED_PIPE_MODE.PIPE_WAIT | NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS,
                PInvoke.PIPE_UNLIMITED_INSTANCES,
                BufferSize,
                0,
                0,
                attributes);
            if (handle.IsInvalid)
            {
                throw new Win32Exception();
            }

            var pipe = new SafePipeHandle(handle.DangerousGetHandle(), ownsHandle: true);
            handle.SetHandleAsInvalid();
            return new NamedPipeServerStream(PipeDirection.Out, isAsync: true, isConnected: false, pipe);
        }
    }
}
