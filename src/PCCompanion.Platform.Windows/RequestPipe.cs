using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A named pipe through which the other programs of the user's account on this PC ask this
/// process something only it can answer, such as the settings app asking the host for Folder
/// Rules' history. A program that connects sends one request, gets one answer, and the pipe
/// closes. Like <see cref="SnapshotPipe"/>, programs on other PCs are rejected. It answers
/// programs on the thread pool, several at once.
/// </summary>
public sealed class RequestPipe : IDisposable
{
    // How long a program that connected may take to send its request and to take the answer.
    private static readonly TimeSpan ProgramTimeLimit = TimeSpan.FromSeconds(10);

    private readonly string _name;
    private readonly int _maximumRequestLength;
    private readonly Func<byte[], Task<byte[]>> _answer;
    private readonly Action<Exception> _failed;
    private readonly CancellationTokenSource _stop = new();
    private NamedPipeServerStream? _pipe;

    private RequestPipe(string name, int maximumRequestLength, Func<byte[], Task<byte[]>> answer, Action<Exception> failed, NamedPipeServerStream pipe)
    {
        _name = name;
        _maximumRequestLength = maximumRequestLength;
        _answer = answer;
        _failed = failed;
        _pipe = pipe;
        _ = Task.Run(ServeAsync);
    }

    /// <summary>
    /// Answers each program that connects to the pipe named <paramref name="name"/> with what
    /// <paramref name="answer"/> returns for its request, until disposed. A request longer than
    /// <paramref name="maximumRequestLength"/> gets no answer. <paramref name="answer"/> runs on
    /// the thread pool, and so does <paramref name="failed"/> when the pipe stops working or an
    /// answer fails.
    /// </summary>
    /// <exception cref="Win32Exception">Windows couldn't create the pipe.</exception>
    public static RequestPipe Serve(string name, int maximumRequestLength, Func<byte[], Task<byte[]>> answer, Action<Exception> failed)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestLength);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(failed);

        return new RequestPipe(name, maximumRequestLength, answer, failed, NamedPipes.CreateDuplex(name));
    }

    /// <summary>
    /// Sends <paramref name="request"/> to the pipe named <paramref name="name"/> and returns the
    /// answer, or <see langword="null"/> when no program serves it within
    /// <paramref name="timeout"/>, the answer isn't given within that time, or it's longer than
    /// <paramref name="maximumLength"/>.
    /// </summary>
    public static async Task<byte[]?> AskAsync(string name, byte[] request, TimeSpan timeout, int maximumLength, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeLimit.Token).ConfigureAwait(false);
            pipe.ReadMode = PipeTransmissionMode.Message;
            await pipe.WriteAsync(request, timeLimit.Token).ConfigureAwait(false);
            return await ReadMessageAsync(pipe, maximumLength, timeLimit.Token).ConfigureAwait(false);
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

    /// <summary>Stops serving. A program asking at that moment may get no answer.</summary>
    public void Dispose()
    {
        _stop.Cancel();

        // Closing the pipe ends its wait for a program at once.
        Interlocked.Exchange(ref _pipe, null)?.Dispose();
    }

    // One message, or null when it's longer than the limit or the other end closed the pipe
    // before it was whole.
    private static async Task<byte[]?> ReadMessageAsync(PipeStream pipe, int maximumLength, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        do
        {
            var read = await pipe.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0 || message.Length + read > maximumLength)
            {
                return null;
            }

            message.Write(buffer, 0, read);
        }
        while (!pipe.IsMessageComplete);

        return message.ToArray();
    }

    // Once a program connects, a pipe of the same name waits for the next one, so that a request
    // whose answer takes a while doesn't hold up the others.
    private async Task ServeAsync()
    {
        try
        {
            while (Volatile.Read(ref _pipe) is { } pipe)
            {
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var next = NamedPipes.CreateDuplex(_name);
                if (Interlocked.CompareExchange(ref _pipe, next, pipe) != pipe)
                {
                    await next.DisposeAsync().ConfigureAwait(false);
                }

                _ = AnswerAsync(pipe);
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

    // Closing the pipe, rather than disconnecting it, leaves what's written for the program to read.
    private async Task AnswerAsync(NamedPipeServerStream pipe)
    {
        await using var connected = pipe;
        try
        {
            byte[]? request;
            using (var reading = ProgramTime())
            {
                request = await ReadMessageAsync(pipe, _maximumRequestLength, reading.Token).ConfigureAwait(false);
            }

            if (request is null)
            {
                return;
            }

            // Working out the answer may take a while, which the program waits for or not.
            var answer = await _answer(request).WaitAsync(_stop.Token).ConfigureAwait(false);
            using var writing = ProgramTime();
            await pipe.WriteAsync(answer, writing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
        {
            // The program didn't send its request or take the answer in time.
        }
        catch (IOException)
        {
            // The program left before it had the answer.
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _failed(exception);
        }
    }

    private CancellationTokenSource ProgramTime()
    {
        var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeLimit.CancelAfter(ProgramTimeLimit);
        return timeLimit;
    }
}
