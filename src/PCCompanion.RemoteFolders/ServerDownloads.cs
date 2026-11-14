using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.RemoteFolders;

/// <summary>Windows' request for a file's data, as <see cref="ServerDownloads"/> answers it.</summary>
public interface IDataRequest
{
    string Path { get; }

    IReadOnlyList<byte> Identity { get; }

    long FileSize { get; }

    long RequiredOffset { get; }

    long RequiredLength { get; }

    /// <summary>Sends one part; see <see cref="FetchDataRequest.Transfer"/>.</summary>
    int Transfer(long offset, ReadOnlySpan<byte> data);

    /// <summary>Tells Windows the download goes on.</summary>
    int Progress(long total, long completed);

    int Refuse(int status);

    void Done();
}

/// <summary>A data request from Windows.</summary>
public sealed class DataRequest(FetchDataRequest request) : IDataRequest
{
    public string Path => request.Path;

    public IReadOnlyList<byte> Identity => request.Identity;

    public long FileSize => request.FileSize;

    public long RequiredOffset => request.RequiredOffset;

    public long RequiredLength => request.RequiredLength;

    public int Transfer(long offset, ReadOnlySpan<byte> data) => request.Transfer(offset, data);

    public int Progress(long total, long completed) => request.Progress(total, completed);

    public int Refuse(int status) => request.Refuse(status);

    public void Done() => request.Done();
}

/// <summary>
/// Downloads files from a server when a program reads them. See "Downloading" in
/// docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class ServerDownloads(
    string remoteFolder,
    IServerConnections connections,
    ServerListings listings,
    TimeProvider time,
    ILogger logger,
    int serverNumber)
{
    /// <summary>How long a download may wait for data before it fails as unreachable.</summary>
    public static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(30);

    /// <summary>How much is read and sent to Windows at a time, a multiple of 4,096.</summary>
    public const int ChunkSize = 1024 * 1024;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Downloads the range Windows asked for. Never throws: a failure refuses the request.</summary>
    public async Task HandleAsync(IDataRequest request)
    {
        if (ItemIdentity.FromBytes([.. request.Identity]) is not { IsDirectory: false } identity)
        {
            request.Refuse(CloudFileStatus.Unsuccessful);
            return;
        }

        using var cancel = new CancellationTokenSource();
        _running[request.Path] = cancel;
        var started = time.GetTimestamp();
        try
        {
            long sent = await connections.UseForTransferAsync((server, cancellation) => SendAsync(server, identity, request, cancellation), cancel.Token).ConfigureAwait(false);
            request.Done();
            double elapsed = time.GetElapsedTime(started).TotalMilliseconds;
            LogDownloaded(logger, serverNumber, sent, elapsed);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            LogCancelled(logger, serverNumber);
        }
        catch (RemoteServerException exception) when (exception.Error == RemoteError.Changed)
        {
            // The file changed on the server since its placeholder was made: its folder is listed again.
            LogFileChanged(logger, serverNumber);
            request.Refuse(CloudFileStatus.Unsuccessful);
            _ = listings.RefreshAsync(Parent(identity.RelativePath));
        }
        catch (RemoteServerException exception)
        {
            LogDownloadFailed(logger, serverNumber, exception.Error);
            request.Refuse(ServerListings.StatusFor(exception.Error));
        }
        catch (Exception exception)
        {
            LogDownloadCrashed(logger, serverNumber, exception);
            request.Refuse(CloudFileStatus.Unsuccessful);
        }
        finally
        {
            _running.TryRemove(new KeyValuePair<string, CancellationTokenSource>(request.Path, cancel));
        }
    }

    /// <summary>Stops a download Windows gave up on, because it timed out or the user cancelled it.</summary>
    public void Cancel(string path)
    {
        if (_running.TryGetValue(path, out var cancel))
        {
            cancel.Cancel();
        }
    }

    private async Task<long> SendAsync(IRemoteServer server, ItemIdentity identity, IDataRequest request, CancellationToken cancellation)
    {
        await using var stream = await server.OpenReadAsync(ItemIdentity.RemotePath(remoteFolder, identity.RelativePath), cancellation).ConfigureAwait(false);
        if (stream.Length != request.FileSize)
        {
            throw Changed();
        }

        long end = Math.Min(request.FileSize, request.RequiredOffset + request.RequiredLength);
        long offset = request.RequiredOffset;
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[ChunkSize];
        while (offset < end)
        {
            int length = (int)Math.Min(ChunkSize, end - offset);
            int read = 0;
            while (read < length)
            {
                using var stall = new CancellationTokenSource(StallLimit, time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stall.Token);
                int got;
                try
                {
                    got = await stream.ReadAsync(buffer.AsMemory(read, length - read), linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stall.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    throw new RemoteServerException(RemoteError.Unreachable, "No data came from the server for 30 seconds.");
                }

                if (got == 0)
                {
                    throw Changed();
                }

                read += got;

                // Windows waits 60 seconds for news of a download; each read is news.
                request.Progress(request.FileSize, offset + read);
            }

            int result = request.Transfer(offset, buffer.AsSpan(0, length));
            if (result != 0)
            {
                // Windows no longer wants the data, for example after a cancel.
                throw new OperationCanceledException($"Windows refused the data (0x{result:X8}).");
            }

            offset += length;
        }

        return offset - request.RequiredOffset;
    }

    private static string Parent(string relativePath) => relativePath[..Math.Max(0, relativePath.LastIndexOf('/'))];

    private static RemoteServerException Changed() => new(RemoteError.Changed, "The file on the server isn't the size its placeholder says.");

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: downloaded {Bytes} bytes in {Milliseconds:F0} ms.")]
    private static partial void LogDownloaded(ILogger logger, int server, long bytes, double milliseconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: a download was cancelled.")]
    private static partial void LogCancelled(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a download failed ({Error}).")]
    private static partial void LogDownloadFailed(ILogger logger, int server, RemoteError error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Server}: a file changed on the server during its download; its folder is listed again.")]
    private static partial void LogFileChanged(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server {Server}: a download failed.")]
    private static partial void LogDownloadCrashed(ILogger logger, int server, Exception exception);
}
