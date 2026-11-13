using System;
using Windows.Win32;
using Windows.Win32.System.Performance;

namespace PCCompanion.Platform.Windows;

/// <summary>A performance counter for how busy the processors of the whole PC are.</summary>
public enum ProcessorCounterKind
{
    /// <summary>
    /// <c>\Processor Information(_Total)\% Processor Time</c>: the share of time the processors
    /// weren't idle.
    /// </summary>
    Time,

    /// <summary>
    /// <c>\Processor Information(_Total)\% Processor Utility</c>, which also accounts for the
    /// processors' speed: it reads higher while they run faster than their base speed.
    /// </summary>
    Utility,
}

/// <summary>
/// How busy the processors of the whole PC are, from a performance counter read through PDH. Both
/// counters cover every processor group. Each reading covers the time since the one before.
/// Opening it takes up to half a second, so open and read it on a thread that may wait. Create,
/// use and dispose it on one thread.
/// </summary>
public sealed class ProcessorCounter : IDisposable
{
    private const int Windows11Build = 22000;

    // PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA.
    private const uint ValidData = 0;
    private const uint NewData = 1;

    private readonly PdhCloseQuerySafeHandle _query;
    private readonly PDH_HCOUNTER _counter;

    private ProcessorCounter(PdhCloseQuerySafeHandle query, PDH_HCOUNTER counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>
    /// The counter that Task Manager's Processes and Performance tabs show on a Windows build:
    /// processor time on Windows 11, where Task Manager has shown it on every page since a 2025
    /// update, and processor utility on Windows 10.
    /// </summary>
    public static ProcessorCounterKind ShownByTaskManager(int windowsBuild) =>
        windowsBuild >= Windows11Build ? ProcessorCounterKind.Time : ProcessorCounterKind.Utility;

    /// <summary>
    /// Opens the counter and takes the first reading. Returns <see langword="null"/> when Windows
    /// can't provide it, for example because performance counters are turned off;
    /// <paramref name="status"/> is then PDH's error code.
    /// </summary>
    public static ProcessorCounter? TryOpen(ProcessorCounterKind kind, out uint status)
    {
        // Added by its English name, so it's found in every Windows language.
        var path = kind switch
        {
            ProcessorCounterKind.Time => @"\Processor Information(_Total)\% Processor Time",
            ProcessorCounterKind.Utility => @"\Processor Information(_Total)\% Processor Utility",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        status = PInvoke.PdhOpenQuery((string?)null, 0, out var query);
        if (status == 0)
        {
            status = PInvoke.PdhAddEnglishCounter(query, path, 0, out var counter);
            if (status == 0)
            {
                status = PInvoke.PdhCollectQueryData((PDH_HQUERY)query.DangerousGetHandle());
                if (status == 0)
                {
                    return new ProcessorCounter(query, counter);
                }
            }
        }

        query.Dispose();
        return null;
    }

    /// <summary>
    /// Reads the counter since the last reading, in percent from 0 to 100, capped at 100 as in
    /// Task Manager: processor utility goes above 100 while processors run faster than their base
    /// speed. Returns false when Windows couldn't provide a value.
    /// </summary>
    public unsafe bool TryRead(out double percent)
    {
        percent = 0;
        if (PInvoke.PdhCollectQueryData((PDH_HQUERY)_query.DangerousGetHandle()) != 0)
        {
            return false;
        }

        PDH_FMT_COUNTERVALUE value;
        if (PInvoke.PdhGetFormattedCounterValue(_counter, PDH_FMT.PDH_FMT_DOUBLE, null, &value) != 0
            || value.CStatus is not (ValidData or NewData))
        {
            return false;
        }

        percent = Math.Clamp(value.Anonymous.doubleValue, 0, 100);
        return true;
    }

    public void Dispose() => _query.Dispose();
}
