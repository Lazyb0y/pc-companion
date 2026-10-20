using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Where the meters' readings come from: Windows, or a fake in tests. All of them are system-wide.
/// </summary>
public interface ISystemReadings
{
    /// <summary>
    /// Opens the processor utility counter and takes its first reading, or returns
    /// <see langword="null"/> with PDH's error code when it isn't available.
    /// </summary>
    IProcessorUtilityReader? OpenProcessorUtility(out uint status);

    /// <summary>Returns false when Windows refused.</summary>
    bool TryReadProcessorTimes(out ProcessorTimes times);

    PhysicalMemory ReadMemory();

    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused.</exception>
    IReadOnlyList<NetworkAdapterCounts> ReadNetworkAdapters();
}

/// <summary>The processor utility counter, open until disposed.</summary>
public interface IProcessorUtilityReader : IDisposable
{
    /// <summary>The utility since the last reading, from 0 to 100. Returns false when the reading failed.</summary>
    bool TryRead(out double percent);
}
