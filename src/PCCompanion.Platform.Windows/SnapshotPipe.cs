using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A named pipe through which this process gives the other programs of the user's account on this
/// PC a snapshot of something only it knows, such as Hot Corners' recent results. It only writes:
/// a program that connects gets the snapshot and the pipe closes, and nothing it could send is
/// read. Programs on other PCs are rejected. It waits for programs on the thread pool.
/// </summary>
public sealed class SnapshotPipe : IDisposable
{
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

        return new SnapshotPipe(name, snapshot, failed, NamedPipes.CreateOutbound(name));
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
                var next = NamedPipes.CreateOutbound(_name);
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
}
