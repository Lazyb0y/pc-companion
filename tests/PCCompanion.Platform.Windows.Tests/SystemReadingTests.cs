using System.Linq;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Reads the PC's system-wide counters; nothing is changed.
public sealed class SystemReadingTests
{
    [Theory]
    [InlineData(ProcessorCounterKind.Time)]
    [InlineData(ProcessorCounterKind.Utility)]
    public void ProcessorCounterIsAPercentage(ProcessorCounterKind kind)
    {
        using var counter = ProcessorCounter.TryOpen(kind, out var status);

        Assert.True(counter is not null, $"PDH status 0x{status:X8}");
        Thread.Sleep(100);
        Assert.True(counter.TryRead(out var percent));
        Assert.InRange(percent, 0, 100);
    }

    [Theory]
    [InlineData(19041, ProcessorCounterKind.Utility)]
    [InlineData(19045, ProcessorCounterKind.Utility)]
    [InlineData(22000, ProcessorCounterKind.Time)]
    [InlineData(26100, ProcessorCounterKind.Time)]
    public void TaskManagerShowsProcessorTimeOnWindows11AndUtilityOnWindows10(int windowsBuild, ProcessorCounterKind kind)
    {
        Assert.Equal(kind, ProcessorCounter.ShownByTaskManager(windowsBuild));
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
