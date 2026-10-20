using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Reads the meters' figures from Windows.
/// </summary>
internal sealed class WindowsSystemReadings : ISystemReadings
{
    public IProcessorUtilityReader? OpenProcessorUtility(out uint status) =>
        ProcessorUtility.TryOpen(out status) is { } utility ? new UtilityReader(utility) : null;

    public bool TryReadProcessorTimes(out ProcessorTimes times) => ProcessorTimes.TryRead(out times);

    public PhysicalMemory ReadMemory() => PhysicalMemory.Read();

    public IReadOnlyList<NetworkAdapterCounts> ReadNetworkAdapters() => NetworkAdapters.ReadCounts();

    private sealed class UtilityReader(ProcessorUtility utility) : IProcessorUtilityReader
    {
        public bool TryRead(out double percent) => utility.TryRead(out percent);

        public void Dispose() => utility.Dispose();
    }
}
