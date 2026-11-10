using System;
using System.Drawing;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class MiddleClickPasteControllerTests : IDisposable
{
    private static readonly Point Here = new(100, 100);

    private readonly FakeMiddleClickPasteSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly MiddleClickPasteController _controller;

    public MiddleClickPasteControllerTests()
    {
        _controller = new MiddleClickPasteController(_system, _clock, action => action(), NullLogger.Instance);
        _controller.Configure(new MiddleClickPasteSettings { Enabled = true });
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void ATextBoxUnderTheRestingPointerIsClaimedForAPlainMiddleClick()
    {
        RestAt(Here);
        _system.Answer(TextBoxStatus.TextBox, window: 55);

        var claim = _system.Claim!;
        Assert.Equal([Here], _system.Asked);
        Assert.Equal(Rectangle.FromLTRB(98, 98, 103, 103), claim.Area);
        Assert.Equal([new PointerTrigger(PointerButton.Middle, PointerKeys.None)], claim.Triggers.ToArray());
        Assert.Equal(55, _controller.Claimed!.Window);
        Assert.Equal(10, _controller.Claimed.Root);
    }

    [Fact]
    public void NothingIsAskedBeforeThePointerRests()
    {
        Move(Here);

        Assert.Empty(_system.Asked);
    }

    [Fact]
    public void WithoutTheIBeamNothingIsAsked()
    {
        _system.IsTextPointer = false;
        RestAt(Here);

        Assert.Empty(_system.Asked);
    }

    [Fact]
    public void WhileAButtonIsHeldNothingIsAsked()
    {
        _system.IsMouseButtonHeld = true;
        RestAt(Here);

        Assert.Empty(_system.Asked);
    }

    [Theory]
    [InlineData(SelectedTextApp.RunsAsAdministrator)]
    [InlineData(SelectedTextApp.RunsWithFewerRights)]
    [InlineData(SelectedTextApp.NotResponding)]
    public void AnAppThatMayNotBeAskedIsLeftAlone(SelectedTextApp app)
    {
        _system.App = app;
        RestAt(Here);

        Assert.Empty(_system.Asked);
    }

    [Fact]
    public void AListedAppIsLeftAlone()
    {
        _controller.Configure(new MiddleClickPasteSettings { Enabled = true, ExcludedApps = [new ListedApp { Name = "Ten", Program = @"C:\Apps\10.exe" }] });
        RestAt(Here);

        Assert.Empty(_system.Asked);
    }

    [Theory]
    [InlineData(TextBoxStatus.NotTextBox)]
    [InlineData(TextBoxStatus.ReadOnly)]
    [InlineData(TextBoxStatus.Password)]
    [InlineData(TextBoxStatus.TimedOut)]
    public void WhatIsntATextBoxIsntClaimedOrAskedAgainThere(TextBoxStatus status)
    {
        RestAt(Here);
        _system.Answer(status);
        RestAt(Here);

        Assert.Null(_system.Claim);
        Assert.Single(_system.Asked);
    }

    [Fact]
    public void TheClaimStaysWhileThePointerMovesWithinTwoPixels()
    {
        Claim();
        RestAt(new Point(102, 98));

        Assert.NotNull(_controller.Claimed);
        Assert.Single(_system.Claims);
    }

    [Fact]
    public void TheClaimGoesAsThePointerLeaves()
    {
        Claim();
        Move(new Point(103, 100));

        Assert.Null(_controller.Claimed);
        Assert.Null(_system.Claim);
    }

    [Fact]
    public void TheWheelRemovesTheClaimAndTheNextWaitsHalfASecond()
    {
        Claim();
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.Wheel, 120, 0));
        _clock.Advance(MiddleClickPasteController.SettleDelay);

        Assert.Null(_system.Claim);
        Assert.Single(_system.Asked);

        _clock.Advance(MiddleClickPasteController.WheelQuiet);
        Assert.Equal(2, _system.Asked.Count);
    }

    [Fact]
    public void APointerThatMovedOnWhileTheAppAnsweredIsLookedAtAgain()
    {
        var there = new Point(300, 200);
        RestAt(Here);
        RestAt(there);
        _system.Answer(TextBoxStatus.TextBox);

        Assert.Null(_system.Claim);
        Assert.Equal([Here, there], _system.Asked);
    }

    [Fact]
    public void AnAnswerAfterThePointerLeftClaimsNothing()
    {
        RestAt(Here);
        _system.PointerPosition = new Point(400, 400);
        _system.Answer(TextBoxStatus.TextBox);

        Assert.Null(_system.Claim);
    }

    [Fact]
    public void StoppingRemovesTheClaim()
    {
        Claim();
        _controller.Stop();

        Assert.Null(_system.Claim);
        Assert.Null(_controller.Claimed);
    }

    [Fact]
    public void ANewListOfAppsLooksAgain()
    {
        Claim();
        _controller.Configure(new MiddleClickPasteSettings { Enabled = true, ExcludedApps = [new ListedApp { Name = "Ten", Program = @"C:\Apps\10.exe" }] });
        _clock.Advance(MiddleClickPasteController.SettleDelay);

        Assert.Null(_system.Claim);
        Assert.Single(_system.Asked);
    }

    private void Move(Point to)
    {
        _system.PointerPosition = to;
        _controller.OnPointerInput(new PointerInputEventArgs(PointerButtonChanges.None, 0, 0));
    }

    private void RestAt(Point point)
    {
        Move(point);
        _clock.Advance(MiddleClickPasteController.SettleDelay);
    }

    private void Claim()
    {
        RestAt(Here);
        _system.Answer(TextBoxStatus.TextBox);
        Assert.NotNull(_system.Claim);
    }
}
