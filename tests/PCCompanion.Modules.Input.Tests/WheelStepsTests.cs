using System;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class WheelStepsTests
{
    private readonly ManualTimeProvider _clock = new();
    private readonly WheelSteps _steps;

    public WheelStepsTests()
    {
        _steps = new WheelSteps(_clock);
    }

    [Fact]
    public void EachNotchOfTheVolumeIsAStepUpOrDown()
    {
        Assert.Equal(1, _steps.Add(HotCornerWheelAction.Volume, 120));
        Assert.Equal(-2, _steps.Add(HotCornerWheelAction.Volume, -240));
    }

    [Fact]
    public void SmallerMovesAddUpToANotch()
    {
        Assert.Equal(0, _steps.Add(HotCornerWheelAction.Volume, 30));
        Assert.Equal(0, _steps.Add(HotCornerWheelAction.Volume, 30));
        Assert.Equal(0, _steps.Add(HotCornerWheelAction.Volume, 30));
        Assert.Equal(1, _steps.Add(HotCornerWheelAction.Volume, 30));
    }

    [Fact]
    public void TurningTheOtherWayStartsANewNotch()
    {
        Assert.Equal(0, _steps.Add(HotCornerWheelAction.Volume, 60));
        Assert.Equal(0, _steps.Add(HotCornerWheelAction.Volume, -60));
        Assert.Equal(-1, _steps.Add(HotCornerWheelAction.Volume, -60));
    }

    [Fact]
    public void ResetForgetsAPartOfANotch()
    {
        _steps.Add(HotCornerWheelAction.Volume, 90);
        _steps.Reset();

        Assert.Equal(0, _steps.Add(HotCornerWheelAction.Volume, 60));
    }

    [Theory]
    [InlineData(HotCornerWheelAction.Desktops)]
    [InlineData(HotCornerWheelAction.Tracks)]
    public void DesktopsAndTracksTakeAtMostOneStepEvery300Milliseconds(HotCornerWheelAction action)
    {
        Assert.Equal(1, _steps.Add(action, 120));

        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0, _steps.Add(action, 120));

        _clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(-1, _steps.Add(action, -360));
    }
}
