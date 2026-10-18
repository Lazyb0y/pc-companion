using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Registers its own hidden window for session messages and posts them to that window only; the
// session itself is never locked.
public sealed class SessionMonitorTests
{
    private const uint SessionChange = 0x02B1; // WM_WTSSESSION_CHANGE

    [Theory]
    [InlineData(0x2, true)] // WTS_CONSOLE_DISCONNECT
    [InlineData(0x4, true)] // WTS_REMOTE_DISCONNECT
    [InlineData(0x7, true)] // WTS_SESSION_LOCK
    [InlineData(0x1, false)] // WTS_CONSOLE_CONNECT
    [InlineData(0x8, false)] // WTS_SESSION_UNLOCK
    public void ReportsLocksAndDisconnects(uint change, bool locked)
    {
        using var window = new MessageWindow(UniqueClassName());
        using var monitor = new SessionMonitor(window);
        var count = 0;
        monitor.Locked += (_, _) => count++;

        window.Post(SessionChange, change);
        RunPostedMessages();

        Assert.Equal(locked ? 1 : 0, count);
    }

    [Fact]
    public void ADisposedMonitorReportsNothing()
    {
        using var window = new MessageWindow(UniqueClassName());
        var monitor = new SessionMonitor(window);
        var count = 0;
        monitor.Locked += (_, _) => count++;

        monitor.Dispose();
        monitor.Dispose();
        window.Post(SessionChange, 0x7);
        RunPostedMessages();

        Assert.Equal(0, count);
    }

    private static string UniqueClassName() => "PCCompanion.Tests." + Guid.NewGuid().ToString("N");

    // Messages posted before Quit are delivered before the loop ends.
    private static void RunPostedMessages()
    {
        MessageLoop.Quit();
        MessageLoop.Run();
    }
}
