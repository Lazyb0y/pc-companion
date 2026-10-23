using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test serves a pipe of its own name, which no other program uses.
public sealed class SnapshotPipeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _name = "PCCompanion.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AProgramThatConnectsGetsTheSnapshot()
    {
        using var pipe = SnapshotPipe.Serve(_name, () => "hello"u8.ToArray(), Fail);

        Assert.Equal("hello", Text(await SnapshotPipe.ReadAsync(_name, Timeout, 1024, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task EachProgramGetsTheSnapshotOfThatMoment()
    {
        var count = 0;
        using var pipe = SnapshotPipe.Serve(_name, () => Encoding.UTF8.GetBytes((++count).ToString(System.Globalization.CultureInfo.InvariantCulture)), Fail);

        Assert.Equal("1", Text(await SnapshotPipe.ReadAsync(_name, Timeout, 1024, TestContext.Current.CancellationToken)));
        Assert.Equal("2", Text(await SnapshotPipe.ReadAsync(_name, Timeout, 1024, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task WithoutAServerThereIsNoSnapshot()
    {
        var watch = Stopwatch.StartNew();

        Assert.Null(await SnapshotPipe.ReadAsync(_name, TimeSpan.FromMilliseconds(300), 1024, TestContext.Current.CancellationToken));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task AfterTheServerStopsThereIsNoSnapshot()
    {
        var pipe = SnapshotPipe.Serve(_name, () => "hello"u8.ToArray(), Fail);
        pipe.Dispose();

        Assert.Null(await SnapshotPipe.ReadAsync(_name, TimeSpan.FromMilliseconds(300), 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASnapshotLongerThanTheMaximumIsNotGiven()
    {
        using var pipe = SnapshotPipe.Serve(_name, () => new byte[2048], Fail);

        Assert.Null(await SnapshotPipe.ReadAsync(_name, Timeout, 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheServerTakesNoInput()
    {
        using var pipe = SnapshotPipe.Serve(_name, () => "hello"u8.ToArray(), Fail);
        await using var writer = new NamedPipeClientStream(".", _name, PipeDirection.Out);

        Assert.ThrowsAny<UnauthorizedAccessException>(() => writer.Connect(1000));
    }

    [Fact]
    public void ASessionsPipeNameEndsWithTheSession()
    {
        using var process = Process.GetCurrentProcess();

        Assert.Equal($"Name.{process.SessionId}", SnapshotPipe.ForSession("Name"));
    }

    private static string? Text(byte[]? snapshot) => snapshot is null ? null : Encoding.UTF8.GetString(snapshot);

    private static void Fail(Exception exception) => Assert.Fail(exception.ToString());
}
