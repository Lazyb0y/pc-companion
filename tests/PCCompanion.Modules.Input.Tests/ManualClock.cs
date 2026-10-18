using System;

namespace PCCompanion.Modules.Input.Tests;

/// <summary>
/// A clock that moves only when a test advances it.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan time) => _ticks += time.Ticks;
}
