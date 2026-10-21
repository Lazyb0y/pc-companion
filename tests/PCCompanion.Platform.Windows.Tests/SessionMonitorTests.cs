using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Registers its own hidden window for session messages and posts them to that window only; the
// session itself is never locked.
public sealed class SessionMonitorTests
{
    private const uint SessionChange = 0x02B1; // WM_WTSSESSION_CHANGE

    [Theory]
    [InlineData(0x2, 1, 0)] // WTS_CONSOLE_DISCONNECT
    [InlineData(0x4, 1, 0)] // WTS_REMOTE_DISCONNECT
    [InlineData(0x7, 1, 0)] // WTS_SESSION_LOCK
    [InlineData(0x1, 0, 1)] // WTS_CONSOLE_CONNECT
    [InlineData(0x3, 0, 1)] // WTS_REMOTE_CONNECT
    [InlineData(0x8, 0, 1)] // WTS_SESSION_UNLOCK
    [InlineData(0x5, 0, 0)] // WTS_SESSION_LOGON
    public void ReportsLocksAndUnlocks(uint change, int locks, int unlocks)
    {
        using var window = new MessageWindow(UniqueClassName());
        using var monitor = new SessionMonitor(window);
        int lockCount = 0, unlockCount = 0;
        monitor.Locked += (_, _) => lockCount++;
        monitor.Unlocked += (_, _) => unlockCount++;

        window.Post(SessionChange, change);
        RunPostedMessages();

        Assert.Equal(locks, lockCount);
        Assert.Equal(unlocks, unlockCount);
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
}
