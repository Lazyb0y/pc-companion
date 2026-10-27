using System.Linq;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Modules.Display.Tests.TestDisplays;

namespace PCCompanion.Modules.Display.Tests;

// A Dell display with HDR off as the main display, and a Samsung display without HDR to its right,
// saved in Windows as they are.
public sealed class DisplaySwitcherTests
{
    private readonly FakeDisplaySystem _system = new();
    private readonly DisplaySwitcher _switcher;
    private readonly DisplayProfile _desk;

    public DisplaySwitcherTests()
    {
        var main = Connected(1);
        var side = Connected(2, "SAM7088");
        _system.Displays.AddRange([main, side]);
        _system.Current = [Path(main, 0, 0, 0, hdr: DisplayHdr.Off), Path(side, 1, 1920, 0)];
        _system.Saved = [.. _system.Current];
        _desk = _system.Read().ToProfile("Desk");
        _switcher = new DisplaySwitcher(_system);
    }

    [Fact]
    public void AChangeOfResolutionIsAppliedWithoutSavingAndAsked()
    {
        var result = _switcher.Switch(WithSide(1680, 1050));

        Assert.Equal(SwitchOutcome.Asking, result.Outcome);
        Assert.Equal(["check", "apply"], _system.Calls);
        Assert.Equal(1680, _system.Current[1].Area.Width);
        Assert.Equal(1920, _system.Saved[1].Area.Width);
        Assert.True(_switcher.IsAsking);
        Assert.Empty(result.NotAsInProfile!);
    }

    [Fact]
    public void KeepingSavesTheSetup()
    {
        var question = _switcher.Switch(WithSide(1680, 1050)).Question;

        Assert.Equal(AnswerOutcome.Kept, _switcher.Answer(question, kept: true));
        Assert.Equal(_system.Current, _system.Saved);
        Assert.False(_switcher.IsAsking);
    }

    [Fact]
    public void RevertingBringsBackTheSetupFromBeforeWithoutSaving()
    {
        var before = _system.Current.ToList();
        var question = _switcher.Switch(WithSide(1680, 1050)).Question;

        Assert.Equal(AnswerOutcome.Reverted, _switcher.Answer(question, kept: false));
        Assert.Equal(before, _system.Current);
        Assert.DoesNotContain("save", _system.Calls);
    }

    [Fact]
    public void WhenWindowsRefusesTheSetupFromBeforeItsSavedSetupComesBack()
    {
        var question = _switcher.Switch(WithSide(1680, 1050)).Question;
        _system.Displays.RemoveAt(1);

        Assert.Equal(AnswerOutcome.RevertedToSaved, _switcher.Answer(question, kept: false));
        Assert.Equal("apply saved", _system.Calls[^1]);
        Assert.Equal([_system.Saved[0]], _system.Current);
    }

    [Fact]
    public void MovingADisplayIsKeptAtOnce()
    {
        var moved = _desk with { Displays = [_desk.Displays[0], _desk.Displays[1] with { X = -1920 }] };

        Assert.Equal(SwitchOutcome.Kept, _switcher.Switch(moved).Outcome);
        Assert.Equal(["check", "apply", "save"], _system.Calls);
        Assert.Equal(-1920, _system.Saved[1].Area.X);
        Assert.False(_switcher.IsAsking);
    }

    [Fact]
    public void ASetupWindowsRefusesChangesNothingAndNamesTheDisplay()
    {
        var before = _system.Current.ToList();

        var result = _switcher.Switch(WithSide(1234, 567));

        Assert.Equal(SwitchOutcome.Refused, result.Outcome);
        Assert.Equal(1610, result.Error);
        Assert.Equal([1], result.ModesNotOffered);
        Assert.Equal(["check"], _system.Calls);
        Assert.Equal(before, _system.Current);
    }

    [Fact]
    public void AProfileWithADisplayThatsntConnectedChangesNothing()
    {
        var third = _desk.Displays[1] with { Model = "BOE0A1E", Connection = Connected(3, "BOE0A1E").DevicePath, X = 3840 };

        Assert.Equal(SwitchOutcome.NotConnected, _switcher.Switch(_desk with { Displays = [.. _desk.Displays, third] }).Outcome);
        Assert.Empty(_system.Calls);
    }

    [Fact]
    public void TheProfileInUseChangesNothing()
    {
        Assert.Equal(SwitchOutcome.AlreadyInUse, _switcher.Switch(_desk).Outcome);
        Assert.Empty(_system.Calls);
    }

    [Fact]
    public void WhenTheUserCantAnswerTheSwitchIsReverted()
    {
        var before = _system.Current.ToList();
        _switcher.Switch(WithSide(1680, 1050));

        Assert.Equal(AnswerOutcome.Reverted, _switcher.Interrupt());
        Assert.Equal(before, _system.Current);
        Assert.Null(_switcher.Interrupt());
    }

    [Fact]
    public void AnotherProfileWhileAskingRevertsToTheSetupKeptLast()
    {
        var before = _system.Current.ToList();
        var first = _switcher.Switch(WithSide(1680, 1050)).Question;
        var second = _switcher.Switch(WithSide(3840, 2160)).Question;

        Assert.NotEqual(first, second);
        Assert.Null(_switcher.Answer(first, kept: false));
        Assert.Equal(AnswerOutcome.Reverted, _switcher.Answer(second, kept: false));
        Assert.Equal(before, _system.Current);
    }

    [Fact]
    public void AnotherProfileWhileAskingIsComparedWithTheSetupKeptLast()
    {
        var question = _switcher.Switch(WithSide(1680, 1050)).Question;
        var moved = _desk with { Displays = [_desk.Displays[0], _desk.Displays[1] with { Y = 200 }] };

        // Only a move from the setup kept last, though the resolution changes from the one asked about.
        Assert.Equal(SwitchOutcome.Kept, _switcher.Switch(moved).Outcome);
        Assert.Null(_switcher.Answer(question, kept: false));
        Assert.Equal(200, _system.Saved[1].Area.Y);
    }

    [Fact]
    public void HdrIsSetAfterTheSetupAndBackWhenReverted()
    {
        var withHdr = _desk with { Displays = [_desk.Displays[0] with { Hdr = true }, _desk.Displays[1]] };

        var result = _switcher.Switch(withHdr);

        Assert.Equal(SwitchOutcome.Asking, result.Outcome);
        Assert.Equal(DisplayHdr.On, _system.Current[0].Hdr);
        Assert.Equal(AnswerOutcome.Reverted, _switcher.Answer(result.Question, kept: false));
        Assert.Equal(DisplayHdr.Off, _system.Current[0].Hdr);
        Assert.Equal(["check", "apply", "hdr on 1", "apply", "hdr off 1"], _system.Calls);
    }

    [Fact]
    public void HdrThatWindowsRefusesIsReported()
    {
        _system.HdrFails = true;

        var result = _switcher.Switch(_desk with { Displays = [_desk.Displays[0] with { Hdr = true }, _desk.Displays[1] with { Width = 1680, Height = 1050 }] });

        Assert.Equal(SwitchOutcome.Asking, result.Outcome);
        Assert.Equal(50, result.HdrError);
        Assert.Equal([0], result.NotAsInProfile);
    }

    private DisplayProfile WithSide(int width, int height) =>
        _desk with { Displays = [_desk.Displays[0], _desk.Displays[1] with { Width = width, Height = height }] };
}
