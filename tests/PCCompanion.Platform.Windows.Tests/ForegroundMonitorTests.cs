using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Watches which window is in front without changing it.
public sealed class ForegroundMonitorTests
{
    [Fact]
    public void AMonitorCanBeDisposedTwiceAndCreatedAgain()
    {
        var monitor = new ForegroundMonitor();
        monitor.Dispose();
        monitor.Dispose();

        using var again = new ForegroundMonitor();
    }
}
