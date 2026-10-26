using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Each test creates its own hidden window with a unique class name, and watches only its own thread.
public sealed class SlowHandlerWatchTests
{
    private const uint SlowMessage = 0x8001; // WM_APP + 1
    private const uint FastMessage = 0x8002;
    private const uint FirstOwnMessage = 0x8000; // WM_APP
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void AHandlerThatTakesLongerThanTheLimitIsReportedWithItsWindowAndMessage()
    {
        var className = UniqueClassName();
        using var window = new MessageWindow(className);
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == SlowMessage)
            {
                Thread.Sleep(120);
            }
        };

        var reported = Watch(() =>
        {
            window.Post(FastMessage);
            window.Post(SlowMessage);
            RunPostedMessages();
        });

        var slow = Assert.Single(reported);
        Assert.Equal((className, SlowMessage), (slow.Source, slow.Message));
        Assert.InRange(slow.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BeginInvokeActionsAreTimedAsTheirWindowsMessages()
    {
        var className = UniqueClassName();
        using var window = new MessageWindow(className);

        var reported = Watch(() =>
        {
            window.BeginInvoke(() => Thread.Sleep(120));
            RunPostedMessages();
        });

        Assert.Equal(className, Assert.Single(reported).Source);
    }

    [Fact]
    public void TimeInAModalLoopDoesntCount()
    {
        using var window = new MessageWindow(UniqueClassName());
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == SlowMessage)
            {
                // As while a menu is open.
                using (SlowHandlerWatch.EnterModal())
                {
                    Thread.Sleep(120);
                }
            }
        };

        var reported = Watch(() =>
        {
            window.Post(SlowMessage);
            RunPostedMessages();
        });

        Assert.Empty(reported);
    }

    [Fact]
    public void AThreadThatDoesntWatchReportsNothing()
    {
        using var window = new MessageWindow(UniqueClassName());
        var reported = new List<SlowHandler>();
        window.MessageReceived += (_, e) =>
        {
            if (e.Message == SlowMessage)
            {
                Thread.Sleep(120);
            }
        };

        // Another thread watches; this one doesn't.
        var other = new Thread(() => SlowHandlerWatch.Watch(Limit, reported.Add));
        other.Start();
        other.Join();
        window.Post(SlowMessage);
        RunPostedMessages();

        Assert.Empty(reported);
    }

    // Watches this thread while action runs, and returns what was reported for the tests' own
    // messages: from WM_APP, which includes BeginInvoke's registered message. Windows' own messages,
    // such as WM_DWMNCRENDERINGCHANGED for a new window, can take longer than the limit on a busy
    // CI runner.
    private static List<SlowHandler> Watch(Action action)
    {
        var reported = new List<SlowHandler>();
        SlowHandlerWatch.Watch(Limit, reported.Add);
        try
        {
            action();
        }
        finally
        {
            SlowHandlerWatch.StopWatching();
        }

        return reported.FindAll(slow => slow.Message >= FirstOwnMessage);
    }
}
