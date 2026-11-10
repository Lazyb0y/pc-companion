using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

// What happens at a kept middle-click on a claimed text box.
public sealed class MiddleClickPastePasteTests : IDisposable
{
    private const nint Box = 55;
    private static readonly Point Here = new(100, 100);
    private static readonly PointerTrigger PlainMiddleClick = new(PointerButton.Middle, PointerKeys.None);

    private readonly FakeMiddleClickPasteSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly MiddleClickPasteController _controller;
    private readonly List<PasteOutcome> _outcomes = [];

    public MiddleClickPastePasteTests()
    {
        _controller = new MiddleClickPasteController(_system, _clock, action => action(), NullLogger.Instance);
        _controller.Configure(new MiddleClickPasteSettings { Enabled = true });
        _controller.Finished += (_, e) => _outcomes.Add(e.Outcome);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void TheSelectedTextIsDroppedWhereTheClickWasAndThenTheBoxGetsTheFocus()
    {
        Claim(window: 0);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal([Here], _system.Reads);
        Assert.Equal(["drop at 100,100: text", "focus 10, box 0, at 100,100"], _system.Steps);
        Assert.Equal([PasteOutcome.Pasted], _outcomes);
        Assert.False(_controller.IsPasting);
    }

    [Fact]
    public void APlainBoxOfWindowsOwnGetsTheTextByMessageAndThenTheFocus()
    {
        _system.Kinds[Box] = EditBoxKind.Plain;
        Claim(Box);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal(["insert 55 at 100,100: text", "focus 10, box 55, at 100,100"], _system.Steps);
        Assert.Equal([PasteOutcome.Pasted], _outcomes);
    }

    [Fact]
    public void ARichBoxHasItsTextCursorPlacedFirst()
    {
        _system.Kinds[Box] = EditBoxKind.Rich;
        Claim(Box);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal(["place at 100,100", "insert 55 at the cursor: text", "focus 10, box 55, at 100,100"], _system.Steps);
        Assert.Equal([PasteOutcome.Pasted], _outcomes);
    }

    [Fact]
    public void APlainBoxTooLongToPlaceByMessageIsPlacedThroughUiAutomation()
    {
        _system.Kinds[Box] = EditBoxKind.Plain;
        _system.InsertResult = EditBoxResult.NeedsPlacing;
        Claim(Box);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal(["insert 55 at 100,100: text", "place at 100,100", "insert 55 at the cursor: text", "focus 10, box 55, at 100,100"], _system.Steps);
        Assert.Equal([PasteOutcome.Pasted], _outcomes);
    }

    [Theory]
    [InlineData(TextDropResult.Refused, PasteOutcome.BoxRefused)]
    [InlineData(TextDropResult.NotUnderPointer, PasteOutcome.BoxRefused)]
    [InlineData(TextDropResult.NotResponding, PasteOutcome.BoxNotResponding)]
    [InlineData(TextDropResult.Busy, PasteOutcome.Busy)]
    public void ADropTheBoxDidntTakeSaysWhyAndGivesNoFocus(TextDropResult result, PasteOutcome outcome)
    {
        _system.DropResult = result;
        Claim(window: 0);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal(["drop at 100,100: text"], _system.Steps);
        Assert.Equal([outcome], _outcomes);
    }

    [Fact]
    public void ABoxThatRefusedTheMessagesGetsNoFocus()
    {
        _system.Kinds[Box] = EditBoxKind.Plain;
        _system.InsertResult = EditBoxResult.NotResponding;
        Claim(Box);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal(["insert 55 at 100,100: text"], _system.Steps);
        Assert.Equal([PasteOutcome.BoxNotResponding], _outcomes);
    }

    [Fact]
    public void ARichBoxWhoseCursorCouldntBePlacedGetsNothing()
    {
        _system.Kinds[Box] = EditBoxKind.Rich;
        _system.Placed = false;
        Claim(Box);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal(["place at 100,100"], _system.Steps);
        Assert.Equal([PasteOutcome.BoxRefused], _outcomes);
    }

    [Theory]
    [InlineData(SelectedTextStatus.NothingSelected, PasteOutcome.NothingSelected)]
    [InlineData(SelectedTextStatus.WholeBoxSelected, PasteOutcome.NothingSelected)]
    [InlineData(SelectedTextStatus.NotShared, PasteOutcome.NotShared)]
    [InlineData(SelectedTextStatus.Password, PasteOutcome.Password)]
    [InlineData(SelectedTextStatus.TooLong, PasteOutcome.TooLong)]
    [InlineData(SelectedTextStatus.TimedOut, PasteOutcome.AppNotResponding)]
    public void EachReadWithoutTextSaysWhyAndPastesNothing(SelectedTextStatus status, PasteOutcome outcome)
    {
        Claim(window: 0);
        Click();
        _system.GiveRead(status);

        Assert.Empty(_system.Steps);
        Assert.Equal([outcome], _outcomes);
    }

    [Theory]
    [InlineData(SelectedTextApp.NoWindow, PasteOutcome.NothingSelected)]
    [InlineData(SelectedTextApp.NotResponding, PasteOutcome.AppNotResponding)]
    [InlineData(SelectedTextApp.RunsAsAdministrator, PasteOutcome.AppRunsAsAdministrator)]
    [InlineData(SelectedTextApp.RunsWithoutAdminRights, PasteOutcome.RunsAsAdministrator)]
    [InlineData(SelectedTextApp.RunsWithFewerRights, PasteOutcome.AppHasFewerRights)]
    public void AnAppWithTheSelectionThatMayNotBeAskedIsntRead(SelectedTextApp app, PasteOutcome outcome)
    {
        _system.WindowApps[_system.FocusWindow] = app;
        Claim(window: 0);
        Click();

        Assert.Empty(_system.Reads);
        Assert.Equal([outcome], _outcomes);
    }

    [Fact]
    public void AClickWhileAPasteIsUnderWayIsBusy()
    {
        Claim(window: 0);
        Click();
        Click();

        Assert.Single(_system.Reads);
        Assert.Equal([PasteOutcome.Busy], _outcomes);
    }

    [Fact]
    public void AReadThatTakesTooLongEndsThePasteAndTheAppIsntAskedAgain()
    {
        Claim(window: 0);
        Click();
        _clock.Advance(MiddleClickPasteController.ReadLimit);
        Click();
        _system.GiveRead(SelectedTextStatus.Read, "late");

        Assert.Single(_system.Reads);
        Assert.Empty(_system.Steps);
        Assert.Equal([PasteOutcome.AppNotResponding, PasteOutcome.AppNotResponding], _outcomes);
    }

    [Fact]
    public void APointerThatMovedAwayBeforeTheDropPastesNothing()
    {
        Claim(window: 0);
        Click();
        _system.PointerPosition = new Point(105, 100);
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Empty(_system.Steps);
        Assert.Equal([PasteOutcome.PointerMoved], _outcomes);
    }

    [Fact]
    public void APointerThatMovedAwayStillPastesIntoWindowsOwnBox()
    {
        _system.Kinds[Box] = EditBoxKind.Plain;
        Claim(Box);
        Click();
        _system.PointerPosition = new Point(300, 300);
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Equal([PasteOutcome.Pasted], _outcomes);
    }

    [Fact]
    public void AClickOutsideTheClaimOrWithAKeyIsntAPaste()
    {
        Claim(window: 0);
        _controller.OnKept(new KeptPointerInputEventArgs(PlainMiddleClick, new Point(200, 200), 0, 0));
        _controller.OnKept(new KeptPointerInputEventArgs(new PointerTrigger(PointerButton.Middle, PointerKeys.Shift), Here, 0, 0));

        Assert.Empty(_system.Reads);
        Assert.Empty(_outcomes);
    }

    [Fact]
    public void AClickTheHookNeverSawSetsItAgain()
    {
        Claim(window: 0);
        _system.Outcomes[7] = PointerInputOutcome.NotSeen;
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.MiddleDown, 0, 7));
        _clock.Advance(MiddleClickPasteController.CheckDelay);

        Assert.Equal(1, _system.HookSetAgain);
    }

    [Fact]
    public void AClickTheHookKeptLeavesItAlone()
    {
        Claim(window: 0);
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.MiddleDown, 0, 7));
        _clock.Advance(MiddleClickPasteController.CheckDelay);

        Assert.Equal(0, _system.HookSetAgain);
    }

    [Fact]
    public void StoppingDuringAReadPastesNothing()
    {
        Claim(window: 0);
        Click();
        _controller.Stop();
        _system.GiveRead(SelectedTextStatus.Read, "text");

        Assert.Empty(_system.Steps);
    }

    private void Claim(nint window)
    {
        _system.PointerPosition = Here;
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.None, 0, 0));
        _clock.Advance(MiddleClickPasteController.SettleDelay);
        _system.Answer(TextBoxStatus.TextBox, window);
        Assert.NotNull(_controller.Claimed);
    }

    private void Click() => _controller.OnKept(new KeptPointerInputEventArgs(PlainMiddleClick, Here, 0, 0));
}
