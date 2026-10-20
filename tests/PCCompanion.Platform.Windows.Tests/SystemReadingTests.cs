using System.Linq;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the PC's system-wide counters; nothing is changed.
public sealed class SystemReadingTests
{
    [Fact]
    public void ProcessorUtilityIsAPercentage()
    {
        using var utility = ProcessorUtility.TryOpen(out var status);

        Assert.True(utility is not null, $"PDH status 0x{status:X8}");
        Thread.Sleep(100);
        Assert.True(utility.TryRead(out var percent));
        Assert.InRange(percent, 0, 100);
    }

    [Fact]
    public void ProcessorTimesGrowAndKernelTimeIncludesIdleTime()
    {
        Assert.True(ProcessorTimes.TryRead(out var first));
        Thread.Sleep(50);
        Assert.True(ProcessorTimes.TryRead(out var second));

        Assert.True(first.Kernel >= first.Idle);
        Assert.True(second.Idle >= first.Idle);
        Assert.True(second.Kernel + second.User > first.Kernel + first.User);
    }

    [Fact]
    public void PhysicalMemoryHasATotalAndLessAvailable()
    {
        var memory = PhysicalMemory.Read();

        Assert.True(memory.Total > 0);
        Assert.InRange(memory.Available, 1UL, memory.Total);
    }

    [Fact]
    public void NetworkAdaptersIncludeTheLoopbackAndHaveUniqueIds()
    {
        var adapters = NetworkAdapters.ReadCounts();

        Assert.Contains(adapters, adapter => adapter.IsLoopback);
        Assert.Equal(adapters.Count, adapters.Select(adapter => adapter.Id).Distinct().Count());
        Assert.DoesNotContain(adapters, adapter => adapter.IsLoopback && adapter.IsHardware);
    }
}
