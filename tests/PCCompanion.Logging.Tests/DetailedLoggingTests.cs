using System;
using Xunit;

namespace PCCompanion.Logging.Tests;

public sealed class DetailedLoggingTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly DetailedLogging _logging;
    private int _changes;

    public DetailedLoggingTests()
    {
        _logging = new DetailedLogging(_clock);
        _logging.Changed += (_, _) => _changes++;
    }

    public void Dispose() => _logging.Dispose();

    [Fact]
    public void IsOffAtFirst()
    {
        Assert.False(_logging.IsOn);
        Assert.Null(_logging.Until);
    }

    [Fact]
    public void TurnsOnUntilTheGivenTime()
    {
        var until = _clock.Now.AddHours(3);

        _logging.TurnOnUntil(until);

        Assert.True(_logging.IsOn);
        Assert.Equal(until, _logging.Until);
        Assert.Equal(1, _changes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-60)]
    public void StaysOffWithoutATimeOrWithATimeThatHasPassed(int? minutesFromNow)
    {
        _logging.TurnOnUntil(minutesFromNow is { } minutes ? _clock.Now.AddMinutes(minutes) : null);

        Assert.False(_logging.IsOn);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void TurnsOffByItselfAtTheEndTime()
    {
        _logging.TurnOnUntil(_clock.Now + DetailedLogging.Duration);

        _clock.Advance(DetailedLogging.Duration - TimeSpan.FromSeconds(1));
        Assert.True(_logging.IsOn);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(_logging.IsOn);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void StaysOnForAtMostTheDuration()
    {
        _logging.TurnOnUntil(_clock.Now.AddDays(30));

        Assert.Equal(_clock.Now + DetailedLogging.Duration, _logging.Until);
        _clock.Advance(DetailedLogging.Duration);
        Assert.False(_logging.IsOn);
    }

    [Fact]
    public void ALaterEndTimeReplacesTheEarlierOne()
    {
        _logging.TurnOnUntil(_clock.Now.AddHours(1));
        _logging.TurnOnUntil(_clock.Now.AddHours(2));

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True(_logging.IsOn);

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.False(_logging.IsOn);
    }

    [Fact]
    public void TurningOffCancelsTheTimer()
    {
        _logging.TurnOnUntil(_clock.Now.AddHours(1));
        _logging.TurnOnUntil(null);
        Assert.False(_logging.IsOn);

        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(2, _changes);
    }
}
