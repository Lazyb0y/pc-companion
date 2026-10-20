using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// How much of the physical memory is in use, as Task Manager shows it: the memory Windows can
/// use, minus what's available.
/// </summary>
/// <param name="Percent">From 0 to 100.</param>
public readonly record struct MemoryUse(double Percent, ulong UsedBytes, ulong TotalBytes)
{
    public static MemoryUse From(PhysicalMemory memory)
    {
        if (memory.Total == 0)
        {
            return default;
        }

        var used = memory.Total - ulong.Min(memory.Available, memory.Total);
        return new MemoryUse(100.0 * used / memory.Total, used, memory.Total);
    }
}
