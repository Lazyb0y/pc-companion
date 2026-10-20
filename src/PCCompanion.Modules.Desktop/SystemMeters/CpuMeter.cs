using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// How busy the processors are: processor utility, as Task Manager shows it, or, when that counter
/// isn't available, the share of time the processors weren't idle (<c>GetSystemTimes</c>). Each
/// reading covers the time since the one before. Use it on one thread.
/// </summary>
public sealed partial class CpuMeter : IDisposable
{
    // Failed readings in a row after which the counter counts as not working.
    private const int FailuresBeforeFallback = 3;

    private readonly ISystemReadings _readings;
    private readonly ILogger _logger;
    private IProcessorUtilityReader? _utility;
    private int _failures;
    private ProcessorTimes? _lastTimes;

    /// <summary>Opens the counter, which takes up to half a second.</summary>
    public CpuMeter(ISystemReadings readings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(logger);

        _readings = readings;
        _logger = logger;
        _utility = readings.OpenProcessorUtility(out var status);
        if (_utility is null)
        {
            LogCounterNotAvailable(logger, status);
            Restart();
        }
    }

    /// <summary>Whether the meter reads processor time because the utility counter isn't available.</summary>
    public bool UsesProcessorTime => _utility is null;

    /// <summary>
    /// How busy the processors were since the last reading, from 0 to 100, or
    /// <see langword="null"/> when there's no reading yet or it failed.
    /// </summary>
    public double? Read()
    {
        if (_utility is not null)
        {
            if (_utility.TryRead(out var percent))
            {
                _failures = 0;
                return Math.Clamp(percent, 0, 100);
            }

            if (++_failures == FailuresBeforeFallback)
            {
                LogCounterStoppedWorking(_logger, FailuresBeforeFallback);
                _utility.Dispose();
                _utility = null;
                Restart();
            }

            return null;
        }

        if (!_readings.TryReadProcessorTimes(out var times))
        {
            return null;
        }

        var previous = _lastTimes;
        _lastTimes = times;
        if (previous is not { } last)
        {
            return null;
        }

        // Kernel time includes idle time.
        var total = times.Kernel - last.Kernel + (times.User - last.User);
        var idle = times.Idle - last.Idle;
        return total > 0 && idle >= 0 && idle <= total ? 100.0 * (total - idle) / total : null;
    }

    /// <summary>Starts over, so the next reading covers only the time from now, as after a pause.</summary>
    public void Restart()
    {
        if (_utility is not null)
        {
            _utility.TryRead(out _);
        }
        else
        {
            _lastTimes = _readings.TryReadProcessorTimes(out var times) ? times : null;
        }
    }

    public void Dispose() => _utility?.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The processor utility counter isn't available (PDH status 0x{Status:X8}), so the CPU meter shows processor time, which can differ from Task Manager.")]
    private static partial void LogCounterNotAvailable(ILogger logger, uint status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The processor utility counter failed {Count} times in a row, so the CPU meter shows processor time from now on.")]
    private static partial void LogCounterStoppedWorking(ILogger logger, int count);
}
