using System.Collections.Generic;
using System.ComponentModel;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.Tests;

/// <summary>
/// Readings that tests set, with a processor utility counter that's available or not.
/// </summary>
internal sealed class FakeSystemReadings : ISystemReadings
{
    public bool UtilityAvailable { get; set; } = true;

    public Queue<double?> Utility { get; } = new();

    public ProcessorTimes? Times { get; set; }

    public PhysicalMemory Memory { get; set; } = new(16UL << 30, 8UL << 30);

    public List<NetworkAdapterCounts> Adapters { get; } = [];

    public bool AdaptersFail { get; set; }

    public int UtilityReads { get; private set; }

    public bool UtilityDisposed { get; private set; }

    public IProcessorUtilityReader? OpenProcessorUtility(out uint status)
    {
        status = UtilityAvailable ? 0 : 0xC0000BB8; // PDH_CSTATUS_NO_OBJECT
        return UtilityAvailable ? new Reader(this) : null;
    }

    public bool TryReadProcessorTimes(out ProcessorTimes times)
    {
        times = Times ?? default;
        return Times is not null;
    }

    public PhysicalMemory ReadMemory() => Memory;

    public IReadOnlyList<NetworkAdapterCounts> ReadNetworkAdapters() =>
        AdaptersFail ? throw new Win32Exception(5) : [.. Adapters];

    private sealed class Reader(FakeSystemReadings readings) : IProcessorUtilityReader
    {
        public bool TryRead(out double percent)
        {
            readings.UtilityReads++;
            var next = readings.Utility.Count > 0 ? readings.Utility.Dequeue() : 0;
            percent = next ?? 0;
            return next is not null;
        }

        public void Dispose() => readings.UtilityDisposed = true;
    }
}
