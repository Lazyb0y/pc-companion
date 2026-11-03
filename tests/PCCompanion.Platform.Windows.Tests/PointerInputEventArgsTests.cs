using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class PointerInputEventArgsTests
{
    [Fact]
    public void AMoveHasNoChangesAndKeepsItsTime()
    {
        var input = PointerInputEventArgs.FromRawMouse(0, 0, 4_000_000_123);

        Assert.Equal(PointerButtonChanges.None, input.Changes);
        Assert.Equal(0, input.WheelDelta);
        Assert.Equal(4_000_000_123u, input.Time);
    }

    [Fact]
    public void AMiddleClickReportsItsPressAndRelease()
    {
        Assert.Equal(PointerButtonChanges.MiddleDown, PointerInputEventArgs.FromRawMouse(0x0010, 0, 1).Changes);
        Assert.Equal(PointerButtonChanges.MiddleUp, PointerInputEventArgs.FromRawMouse(0x0020, 0, 1).Changes);
    }

    [Theory]
    [InlineData((ushort)120, (short)120)]
    [InlineData((ushort)0xFF88, (short)-120)]
    [InlineData((ushort)30, (short)30)]
    public void TheVerticalWheelReportsHowFarItTurned(ushort data, short delta)
    {
        var input = PointerInputEventArgs.FromRawMouse(0x0400, data, 1);

        Assert.Equal(PointerButtonChanges.Wheel, input.Changes);
        Assert.Equal(delta, input.WheelDelta);
    }

    [Fact]
    public void TheHorizontalWheelGivesNoVerticalDelta()
    {
        var input = PointerInputEventArgs.FromRawMouse(0x0800, 120, 1);

        Assert.Equal(PointerButtonChanges.HorizontalWheel, input.Changes);
        Assert.Equal(0, input.WheelDelta);
    }

    [Fact]
    public void ButtonDataWithoutTheWheelIsNoDelta()
    {
        var input = PointerInputEventArgs.FromRawMouse(0x0001, 120, 1);

        Assert.Equal(PointerButtonChanges.LeftDown, input.Changes);
        Assert.Equal(0, input.WheelDelta);
    }

    [Fact]
    public void SeveralChangesInOneEventAreKeptAndUnknownFlagsDropped()
    {
        var input = PointerInputEventArgs.FromRawMouse(0xF000 | 0x0010 | 0x0004, 0, 1);

        Assert.Equal(PointerButtonChanges.MiddleDown | PointerButtonChanges.RightDown, input.Changes);
    }
}
