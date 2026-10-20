using System;

namespace PCCompanion.Modules.Desktop.Tests;

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>, for wall-clock time and for
/// the timestamps that measure elapsed time.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Now.UtcTicks;

    public void Advance(TimeSpan time) => Now += time;
}
