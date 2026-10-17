using System;
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace PCCompanion.Logging;

/// <summary>
/// Adds detailed (Debug) entries to the file log until a given time; then they turn off by themselves.
/// </summary>
public sealed class DetailedLogging : IDisposable
{
    /// <summary>How long detailed logging stays on when it is turned on, and the longest it can stay on.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(24);

    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private ITimer? _timer;
    private int _generation;

    public DetailedLogging()
        : this(TimeProvider.System)
    {
    }

    public DetailedLogging(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Raised, on any thread, when detailed logging turns on or off or its end time changes.</summary>
    public event EventHandler? Changed;

    /// <summary>When detailed logging turns off, or <see langword="null"/> when it is off.</summary>
    public DateTimeOffset? Until { get; private set; }

    public bool IsOn => Until is not null;

    internal LoggingLevelSwitch LevelSwitch { get; } = new(LogEventLevel.Information);

    /// <summary>
    /// Adds detailed entries until <paramref name="until"/>, but for no longer than <see cref="Duration"/>
    /// from now. <see langword="null"/> or a time that has passed turns them off.
    /// </summary>
    public void TurnOnUntil(DateTimeOffset? until)
    {
        bool changed;
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            var generation = ++_generation;

            var now = _time.GetUtcNow();
            DateTimeOffset? end = until > now ? (until.Value - now > Duration ? now + Duration : until.Value) : null;
            changed = end != Until;
            Until = end;
            LevelSwitch.MinimumLevel = end is null ? LogEventLevel.Information : LogEventLevel.Debug;

            if (end is { } turnOff)
            {
                _timer = _time.CreateTimer(_ => TurnOff(generation), null, turnOff - now, Timeout.InfiniteTimeSpan);
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _generation++;
        }
    }

    // A timer from an earlier TurnOnUntil call can still fire; only the latest one counts.
    private void TurnOff(int generation)
    {
        lock (_lock)
        {
            if (generation != _generation || Until is null)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            Until = null;
            LevelSwitch.MinimumLevel = LogEventLevel.Information;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
