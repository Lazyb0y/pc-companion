using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test creates its own hidden window with a unique class name and touches no other window.
public sealed class MessageWindowTests
{
    private const uint TestMessage = 0x8001; // WM_APP + 1

    [Fact]
    public void DeliversPostedMessages()
    {
        using var window = new MessageWindow(UniqueClassName());
        var received = new List<(uint Message, nuint WParam, nint LParam)>();
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == TestMessage)
            {
                received.Add((e.Message, e.WParam, e.LParam));
                e.Handled = true;
            }
        };

        window.Post(TestMessage, 1, 2);
        RunPostedMessages();

        Assert.Equal([(TestMessage, (nuint)1, (nint)2)], received);
    }

    [Fact]
    public void PostToWindowFindsTheWindowByClassName()
    {
        var className = UniqueClassName();
        using var window = new MessageWindow(className);
        var message = MessageWindow.RegisterMessage("PCCompanion.Tests.Ping");
        var count = 0;
        window.MessageReceived += (_, e) => count += e.Message == message ? 1 : 0;

        Assert.True(MessageWindow.PostToWindow(className, message));
        RunPostedMessages();

        Assert.Equal(1, count);
    }

    [Fact]
    public void AMessageCanBeAllowedFromLowerIntegrityProcesses()
    {
        using var window = new MessageWindow(UniqueClassName());

        Assert.True(window.AllowFromLowerIntegrity(TestMessage));
    }

    [Fact]
    public void PostToWindowReturnsFalseWithoutTheWindow()
    {
        Assert.False(MessageWindow.PostToWindow(UniqueClassName(), TestMessage));
    }

    [Fact]
    public void BeginInvokeRunsActionsOnTheWindowThreadInOrder()
    {
        using var window = new MessageWindow(UniqueClassName());
        var windowThread = Environment.CurrentManagedThreadId;
        var calls = new List<(int Order, int Thread)>();
        var queued = new List<bool>();

        var otherThread = new Thread(() =>
        {
            queued.Add(window.BeginInvoke(() => calls.Add((1, Environment.CurrentManagedThreadId))));
            queued.Add(window.BeginInvoke(() => calls.Add((2, Environment.CurrentManagedThreadId))));
        });
        otherThread.Start();
        otherThread.Join();
        RunPostedMessages();

        Assert.Equal([true, true], queued);
        Assert.Equal([(1, windowThread), (2, windowThread)], calls);
    }

    [Fact]
    public void BeginInvokeReturnsFalseAfterTheWindowIsDisposed()
    {
        var window = new MessageWindow(UniqueClassName());
        window.Dispose();
        var called = false;

        Assert.False(window.BeginInvoke(() => called = true));
        RunPostedMessages();
        Assert.False(called);
    }

    [Fact]
    public void FindOwnerProcessFindsTheProcessThatCreatedTheWindow()
    {
        var className = UniqueClassName();
        using var window = new MessageWindow(className);

        Assert.Equal((uint)Environment.ProcessId, MessageWindow.FindOwnerProcess(className));
        Assert.Null(MessageWindow.FindOwnerProcess(UniqueClassName()));
    }

    [Theory]
    [InlineData(0x0010u, 0u, true)] // WM_CLOSE
    [InlineData(0x0016u, 1u, true)] // WM_ENDSESSION, ending
    [InlineData(0x0016u, 0u, false)] // WM_ENDSESSION, not ending after all
    [InlineData(0x0011u, 0u, false)] // WM_QUERYENDSESSION only asks
    [InlineData(0x0002u, 0u, false)] // WM_DESTROY
    public void IsCloseRequestRecognizesTheMessagesThatCloseAProgram(uint message, uint wParam, bool expected)
    {
        Assert.Equal(expected, MessageWindow.IsCloseRequest(message, wParam));
    }

    [Fact]
    public void ADisposedWindowCanBeCreatedAgain()
    {
        var className = UniqueClassName();
        new MessageWindow(className).Dispose();

        using var window = new MessageWindow(className);

        Assert.NotEqual(0, window.Handle);
    }
}
