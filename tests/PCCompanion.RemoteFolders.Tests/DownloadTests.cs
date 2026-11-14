using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.RemoteFolders.Tests;

/// <summary>A data request that collects what's sent.</summary>
public sealed class FakeDataRequest(string path, byte[] identity, long fileSize) : IDataRequest
{
    private readonly byte[] _data = new byte[fileSize];

    public string Path => path;

    public IReadOnlyList<byte> Identity => identity;

    public long FileSize => _data.Length;

    public long RequiredOffset { get; init; }

    public long RequiredLength { get; init; } = fileSize;

    public List<(long Offset, int Length)> Transfers { get; } = [];

    public int ProgressReports { get; private set; }

    public int? Refused { get; private set; }

    public bool IsDone { get; private set; }

    /// <summary>When set, Windows refuses the data, as after a cancel.</summary>
    public int TransferResult { get; set; }

    public byte[] Data => _data;

    public int Transfer(long offset, ReadOnlySpan<byte> data)
    {
        Transfers.Add((offset, data.Length));
        data.CopyTo(_data.AsSpan((int)offset));
        return TransferResult;
    }

    public int Progress(long total, long completed)
    {
        ProgressReports++;
        return 0;
    }

    public int Refuse(int status)
    {
        Refused = status;
        return 0;
    }

    public void Done() => IsDone = true;
}

/// <summary>A stream that never answers, like a server that went quiet.</summary>
public sealed class StalledStream(long length) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position { get; set; }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = offset;

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

public sealed class DownloadTests : IDisposable
{
    private const string Root = @"C:\Users\Ana\Remote Folders\Home";
    private readonly FakeRemoteServer _server = new();
    private readonly FakePlaceholderSystem _pc = new();
    private readonly ManualTimeProvider _time = new();
    private readonly ServerDownloads _downloads;

    public DownloadTests()
    {
        var connections = new FakeConnections(_server);
        var listings = new ServerListings(Root, "/home/ana", connections, _pc, _time, NullLogger.Instance, 1);
        _downloads = new ServerDownloads("/home/ana", connections, listings, _time, NullLogger.Instance, 1);
    }

    public void Dispose() => _server.Dispose();

    private static byte[] Identity(string relative, long size) => ItemIdentity.Of("", relative, false, size, DateTime.UnixEpoch).ToBytes();

    private static FakeDataRequest Request(string name, long size) => new(Path.Join(Root, name), Identity(name, size), size);

    [Fact]
    public async Task AFileIsSentInChunks()
    {
        string content = new('x', ServerDownloads.ChunkSize * 2 + 100);
        _server.AddFile("/home/ana/big.bin", content);
        var request = Request("big.bin", content.Length);

        await _downloads.HandleAsync(request);

        Assert.True(request.IsDone);
        Assert.Null(request.Refused);
        Assert.Equal([(0L, ServerDownloads.ChunkSize), (ServerDownloads.ChunkSize, ServerDownloads.ChunkSize), (ServerDownloads.ChunkSize * 2L, 100)], request.Transfers);
        Assert.Equal(content, System.Text.Encoding.UTF8.GetString(request.Data));
        Assert.True(request.ProgressReports >= 3);
    }

    [Fact]
    public async Task OnlyTheRequiredRangeIsSent()
    {
        string content = new('y', ServerDownloads.ChunkSize + 10);
        _server.AddFile("/home/ana/part.bin", content);
        var request = new FakeDataRequest(Path.Join(Root, "part.bin"), Identity("part.bin", content.Length), content.Length) { RequiredOffset = ServerDownloads.ChunkSize, RequiredLength = 10 };

        await _downloads.HandleAsync(request);

        Assert.Equal([(ServerDownloads.ChunkSize * 1L, 10)], request.Transfers);
    }

    [Fact]
    public async Task AnUnreachableServerFailsTheDownloadAsNetworkUnavailable()
    {
        _server.AddFile("/home/ana/a.txt", "abc");
        _server.Failure = RemoteError.Unreachable;
        var request = Request("a.txt", 3);

        await _downloads.HandleAsync(request);

        Assert.Equal(CloudFileStatus.NetworkUnavailable, request.Refused);
        Assert.False(request.IsDone);
    }

    [Fact]
    public async Task AFileOfAnotherSizeFailsAndItsFolderIsListedAgain()
    {
        _server.AddFile("/home/ana/a.txt", "changed on the server");
        var request = Request("a.txt", 3);

        await _downloads.HandleAsync(request);

        Assert.Equal(CloudFileStatus.Unsuccessful, request.Refused);
        await WaitUntil(() => _server.Listings == 1);
    }

    [Fact]
    public async Task AStalledServerFailsTheDownloadAfter30Seconds()
    {
        var stalled = new StalledServer(new StalledStream(10));
        var downloads = new ServerDownloads("/home/ana", new FakeConnections(_server) { Transfer = stalled }, null!, _time, NullLogger.Instance, 1);
        var request = Request("a.txt", 10);

        var handling = downloads.HandleAsync(request);
        await WaitUntil(() => stalled.Opened);
        _time.Advance(ServerDownloads.StallLimit);
        await handling;

        Assert.Equal(CloudFileStatus.NetworkUnavailable, request.Refused);
    }

    [Fact]
    public async Task ACancelledDownloadStops()
    {
        var stalled = new StalledServer(new StalledStream(10));
        var downloads = new ServerDownloads("/home/ana", new FakeConnections(_server) { Transfer = stalled }, null!, _time, NullLogger.Instance, 1);
        var request = Request("a.txt", 10);

        var handling = downloads.HandleAsync(request);
        await WaitUntil(() => stalled.Opened);
        downloads.Cancel(request.Path);
        await handling;

        Assert.Null(request.Refused);
        Assert.False(request.IsDone);
    }

    [Fact]
    public async Task DataWindowsRefusesStopsTheDownload()
    {
        _server.AddFile("/home/ana/big.bin", new string('z', ServerDownloads.ChunkSize * 2));
        var request = Request("big.bin", ServerDownloads.ChunkSize * 2);
        request.TransferResult = unchecked((int)0x8007018E);

        await _downloads.HandleAsync(request);

        Assert.Single(request.Transfers);
        Assert.False(request.IsDone);
    }

    [Fact]
    public async Task AFolderOrAForeignPlaceholderIsRefused()
    {
        var folder = new FakeDataRequest(Path.Join(Root, "docs"), ItemIdentity.Of("", "docs", true, 0, DateTime.UnixEpoch).ToBytes(), 0);
        var foreign = new FakeDataRequest(Path.Join(Root, "x"), [7, 7], 1);

        await _downloads.HandleAsync(folder);
        await _downloads.HandleAsync(foreign);

        Assert.Equal(CloudFileStatus.Unsuccessful, folder.Refused);
        Assert.Equal(CloudFileStatus.Unsuccessful, foreign.Refused);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private sealed class StalledServer(Stream stream) : IRemoteServer
    {
        public bool Opened { get; private set; }

        public string HomeFolder => "/";

        public bool IsConnected => true;

        public bool CanReplaceAtOnce => true;

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellation)
        {
            Opened = true;
            return Task.FromResult(stream);
        }

        public Task<RemoteEntry?> StatAsync(string path, CancellationToken cancellation) => throw new NotSupportedException();

        public Task<RemoteListing> ListAsync(string folder, int limit, CancellationToken cancellation) => throw new NotSupportedException();

        public Task UploadAsync(Stream content, string path, CancellationToken cancellation) => throw new NotSupportedException();

        public Task RenameAsync(string source, string destination, CancellationToken cancellation) => throw new NotSupportedException();

        public Task ReplaceAsync(string source, string destination, CancellationToken cancellation) => throw new NotSupportedException();

        public Task DeleteFileAsync(string path, CancellationToken cancellation) => throw new NotSupportedException();

        public Task DeleteFolderAsync(string path, CancellationToken cancellation) => throw new NotSupportedException();

        public Task CreateFolderAsync(string path, CancellationToken cancellation) => throw new NotSupportedException();

        public void Dispose() => stream.Dispose();
    }
}
