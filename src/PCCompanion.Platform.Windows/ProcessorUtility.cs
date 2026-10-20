using System;
using Windows.Win32;
using Windows.Win32.System.Performance;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// How busy the processors of the whole PC are, as Task Manager's Processes and Performance tabs
/// show it: the performance counter <c>\Processor Information(_Total)\% Processor Utility</c>,
/// read through PDH. It accounts for the processors' speed and covers every processor group. Each
/// reading covers the time since the one before. Opening it takes up to half a second, so open
/// and read it on a thread that may wait. Create, use and dispose it on one thread.
/// </summary>
public sealed class ProcessorUtility : IDisposable
{
    // Added by its English name, so it's found in every Windows language.
    private const string CounterPath = @"\Processor Information(_Total)\% Processor Utility";

    // PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA.
    private const uint ValidData = 0;
    private const uint NewData = 1;

    private readonly PdhCloseQuerySafeHandle _query;
    private readonly PDH_HCOUNTER _counter;

    private ProcessorUtility(PdhCloseQuerySafeHandle query, PDH_HCOUNTER counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>
    /// Opens the counter and takes the first reading. Returns <see langword="null"/> when Windows
    /// can't provide it, for example because performance counters are turned off;
    /// <paramref name="status"/> is then PDH's error code.
    /// </summary>
    public static ProcessorUtility? TryOpen(out uint status)
    {
        status = PInvoke.PdhOpenQuery((string?)null, 0, out var query);
        if (status == 0)
        {
            status = PInvoke.PdhAddEnglishCounter(query, CounterPath, 0, out var counter);
            if (status == 0)
            {
                status = PInvoke.PdhCollectQueryData((PDH_HQUERY)query.DangerousGetHandle());
                if (status == 0)
                {
                    return new ProcessorUtility(query, counter);
                }
            }
        }

        query.Dispose();
        return null;
    }

    /// <summary>
    /// Reads the utility since the last reading, in percent from 0 to 100, capped at 100 as in
    /// Task Manager: it goes above 100 while processors run faster than their base speed.
    /// Returns false when Windows couldn't provide a value.
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
