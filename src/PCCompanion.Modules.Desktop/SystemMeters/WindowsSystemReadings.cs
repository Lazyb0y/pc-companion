using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Reads the meters' figures from Windows.
/// </summary>
internal sealed class WindowsSystemReadings : ISystemReadings
{
    public IProcessorCounterReader? OpenProcessorCounter(out uint status) =>
        ProcessorCounter.TryOpen(ProcessorCounter.ShownByTaskManager(Environment.OSVersion.Version.Build), out status) is { } counter
            ? new CounterReader(counter)
            : null;

    public bool TryReadProcessorTimes(out ProcessorTimes times) => ProcessorTimes.TryRead(out times);

    public PhysicalMemory ReadMemory() => PhysicalMemory.Read();

    public IReadOnlyList<NetworkAdapterCounts> ReadNetworkAdapters() => NetworkAdapters.ReadCounts();

    private sealed class CounterReader(ProcessorCounter counter) : IProcessorCounterReader
    {
        public bool TryRead(out double percent) => counter.TryRead(out percent);

        public void Dispose() => counter.Dispose();
    }
}
