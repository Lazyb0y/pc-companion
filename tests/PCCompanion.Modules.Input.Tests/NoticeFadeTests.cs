using System;
using PCCompanion.Modules.Input.LockKeys;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class NoticeFadeTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

    private static TimeSpan Milliseconds(int value) => TimeSpan.FromMilliseconds(value);

    [Fact]
    public void WithAnimationsItFadesInShowsThenFadesOut()
    {
        Assert.Equal((byte)0, NoticeFade.Opacity(TimeSpan.Zero, Duration, animations: true));
        Assert.Equal((byte)128, NoticeFade.Opacity(Milliseconds(50), Duration, animations: true));
        Assert.Equal((byte)255, NoticeFade.Opacity(Milliseconds(100), Duration, animations: true));
        Assert.Equal((byte)255, NoticeFade.Opacity(Milliseconds(1999), Duration, animations: true));
        Assert.Equal((byte)127, NoticeFade.Opacity(Milliseconds(2100), Duration, animations: true));
        Assert.Null(NoticeFade.Opacity(Milliseconds(2200), Duration, animations: true));
    }

    [Fact]
    public void WithoutAnimationsItShowsForItsDurationOnly()
    {
        Assert.Equal((byte)255, NoticeFade.Opacity(TimeSpan.Zero, Duration, animations: false));
        Assert.Equal((byte)255, NoticeFade.Opacity(Milliseconds(1999), Duration, animations: false));
        Assert.Null(NoticeFade.Opacity(Duration, Duration, animations: false));
    }

    [Fact]
    public void GivingWayToTheBadgeItEndsWithoutFadingOut()
    {
        Assert.Equal((byte)255, NoticeFade.Opacity(Milliseconds(1999), Duration, animations: true, fadeOut: false));
        Assert.Null(NoticeFade.Opacity(Duration, Duration, animations: true, fadeOut: false));
        Assert.Null(NoticeFade.NextChange(Duration, Duration, animations: true, fadeOut: false));
    }

    [Fact]
    public void TheNextChangeComesEveryStepWhileFadingAndAtTheEndOfItsTimeOtherwise()
    {
        Assert.Equal(NoticeFade.Step, NoticeFade.NextChange(Milliseconds(20), Duration, animations: true));
        Assert.Equal(Milliseconds(1900), NoticeFade.NextChange(Milliseconds(100), Duration, animations: true));
        Assert.Equal(NoticeFade.Step, NoticeFade.NextChange(Milliseconds(2050), Duration, animations: true));
        Assert.Null(NoticeFade.NextChange(Milliseconds(2200), Duration, animations: true));
        Assert.Equal(Milliseconds(1500), NoticeFade.NextChange(Milliseconds(500), Duration, animations: false));
        Assert.Null(NoticeFade.NextChange(Duration, Duration, animations: false));
    }
}
