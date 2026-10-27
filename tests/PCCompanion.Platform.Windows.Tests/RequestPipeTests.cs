using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test serves a pipe of its own name, which no other program uses.
public sealed class RequestPipeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _name = "PCCompanion.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AProgramGetsTheAnswerToItsRequest()
    {
        using var pipe = RequestPipe.Serve(_name, 1024, request => Task.FromResult(Encoding.UTF8.GetBytes("you said " + Encoding.UTF8.GetString(request))), Fail);

        Assert.Equal("you said hello", Text(await AskAsync("hello"u8.ToArray(), 1024)));
    }

    [Fact]
    public async Task EachProgramGetsItsOwnAnswer()
    {
        using var pipe = RequestPipe.Serve(_name, 1024, Task.FromResult, Fail);

        Assert.Equal("one", Text(await AskAsync("one"u8.ToArray(), 1024)));
        Assert.Equal("two", Text(await AskAsync("two"u8.ToArray(), 1024)));
    }

    [Fact]
    public async Task LongMessagesArriveWhole()
    {
        var big = new byte[200_000];
        Random.Shared.NextBytes(big);
        using var pipe = RequestPipe.Serve(_name, 300_000, Task.FromResult, Fail);

        Assert.Equal(big, await AskAsync(big, 300_000));
    }

    [Fact]
    public async Task ARequestLongerThanTheMaximumGetsNoAnswer()
    {
        var asked = false;
        using var pipe = RequestPipe.Serve(_name, 16, request =>
        {
            asked = true;
            return Task.FromResult(request);
        }, Fail);

        Assert.Null(await AskAsync(new byte[100], 1024));
        Assert.False(asked);
    }

    [Fact]
    public async Task AnAnswerLongerThanTheMaximumIsNotTaken()
    {
        using var pipe = RequestPipe.Serve(_name, 1024, _ => Task.FromResult(new byte[2048]), Fail);

        Assert.Null(await AskAsync("x"u8.ToArray(), 1024));
    }

    [Fact]
    public async Task WithoutAServerThereIsNoAnswer()
    {
        Assert.Null(await RequestPipe.AskAsync(_name, "x"u8.ToArray(), TimeSpan.FromMilliseconds(300), 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AfterTheServerStopsThereIsNoAnswer()
    {
        var pipe = RequestPipe.Serve(_name, 1024, Task.FromResult, Fail);
        pipe.Dispose();

        Assert.Null(await RequestPipe.AskAsync(_name, "x"u8.ToArray(), TimeSpan.FromMilliseconds(300), 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAnswerThatDoesntComeInTimeIsNone()
    {
        var never = new TaskCompletionSource<byte[]>();
        using var pipe = RequestPipe.Serve(_name, 1024, _ => never.Task, Fail);

        Assert.Null(await RequestPipe.AskAsync(_name, "x"u8.ToArray(), TimeSpan.FromMilliseconds(500), 1024, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AProgramThatLeavesBeforeTheAnswerDoesntStopThePipe()
    {
        var slow = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        using var pipe = RequestPipe.Serve(_name, 1024, request =>
        {
            if (first)
            {
                first = false;
                return slow.Task;
            }

            return Task.FromResult(request);
        }, Fail);

        Assert.Null(await RequestPipe.AskAsync(_name, "x"u8.ToArray(), TimeSpan.FromMilliseconds(300), 1024, TestContext.Current.CancellationToken));
        slow.SetResult("late"u8.ToArray());

        Assert.Equal("again", Text(await AskAsync("again"u8.ToArray(), 1024)));
    }

    [Fact]
    public async Task AnAnswerThatTakesAWhileDoesntHoldUpTheOthers()
    {
        var slow = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pipe = RequestPipe.Serve(_name, 1024, request => Text(request) == "slow" ? slow.Task : Task.FromResult(request), Fail);

        var waiting = AskAsync("slow"u8.ToArray(), 1024);
        Assert.Equal("quick", Text(await AskAsync("quick"u8.ToArray(), 1024)));
        Assert.False(waiting.IsCompleted);

        slow.SetResult("done"u8.ToArray());
        Assert.Equal("done", Text(await waiting));
    }

    [Fact]
    public async Task AFailedAnswerIsReportedAndThePipeGoesOn()
    {
        var failures = new ConcurrentQueue<Exception>();
        using var pipe = RequestPipe.Serve(
            _name,
            1024,
            request => Text(request) == "fail" ? Task.FromException<byte[]>(new InvalidOperationException("failed")) : Task.FromResult(request),
            failures.Enqueue);

        Assert.Null(await AskAsync("fail"u8.ToArray(), 1024));
        Assert.Equal("again", Text(await AskAsync("again"u8.ToArray(), 1024)));
        Assert.IsType<InvalidOperationException>(Assert.Single(failures));
    }

    private Task<byte[]?> AskAsync(byte[] request, int maximumLength) =>
        RequestPipe.AskAsync(_name, request, Timeout, maximumLength, TestContext.Current.CancellationToken);

    private static string? Text(byte[]? answer) => answer is null ? null : Encoding.UTF8.GetString(answer);

    private static void Fail(Exception exception) => Assert.Fail(exception.ToString());
}
