using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Modules.Windows.WindowShading;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class ShadingControllerTests : IDisposable
{
    private const nint Mail = 1;
    private const nint Notes = 2;
    private static readonly Rectangle MailFrame = new(100, 200, 800, 600);

    private readonly ManualTimeProvider _clock = new();
    private readonly Queue<Action> _thread = new();
    private readonly FakeShadingSystem _system = new();
    private readonly RecordingLogger<ShadingController> _logger = new();
    private readonly ShadedWindows _set = new();
    private readonly ShadingController _controller;

    public ShadingControllerTests()
    {
        _controller = new ShadingController(_system, _clock, _thread.Enqueue, _logger, _set);
        _controller.Apply(new WindowShadingSettings { Enabled = true });
        _system.Add(Mail, MailFrame);
        _system.Add(Notes, new Rectangle(1000, 100, 600, 400)).Title = "Notes";
        _system.Foreground = Mail;
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void RollingUpMinimizesTheWindowAndShowsItsTitleBarInAStrip()
    {
        _controller.RollUp(Mail);

        var strip = _system.Strips[0];
        Assert.Equal(Rectangle.FromLTRB(101, 201, 899, 231), strip.Bounds);
        Assert.Equal(new Rectangle(8, 1, 798, 30), strip.Source);
        Assert.True(strip.Topmost);
        Assert.Equal("Mail, rolled up", strip.Name);
        Assert.True(_system[Mail].Minimized);
        Assert.Contains("minimize 1 in front True", _system.Calls);
        Assert.Contains(Mail, _system.Marks);
        Assert.Contains(Mail, _system.Watched);
        Assert.True(_set.Contains(Mail));
        Assert.Equal(1, _controller.Count);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("31 pixels high at 96 DPI, found by Measured", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowBehindIsMinimizedWithoutTakingTheFront()
    {
        _controller.RollUp(Notes);

        Assert.Contains("minimize 2 in front False", _system.Calls);
        Assert.Equal(Mail, _system.Foreground);
    }

    [Fact]
    public void WithoutAnAnswerTheButtonsOrWindowsCaptionHeightIsUsedAndTheBandIsTheMost()
    {
        _system[Mail].Measured = null;
        _system[Mail].Buttons = new Rectangle(660, 0, 146, 35);
        _controller.RollUp(Mail);
        Assert.Equal(35, _system.Strips[0].Bounds.Bottom - MailFrame.Top);
        _controller.RollDownAll();

        _system[Mail].Buttons = null;
        _controller.RollUp(Mail);
        Assert.Equal(31, _system.Strips[1].Bounds.Bottom - MailFrame.Top);
        _controller.RollDownAll();

        _system[Mail].Measured = 500;
        _system[Mail].Dpi = 192;
        _controller.RollUp(Mail);
        Assert.Contains("measure 1 band 128", _system.Calls);
        Assert.Equal(128, _system.Strips[2].Bounds.Bottom - MailFrame.Top);
    }

    [Fact]
    public void AWindowThatCantBeRolledUpPlaysTheWarningSound()
    {
        _system[Mail].Refusal = ShadeRefusal.DialogOpen;
        _controller.RollUp(Mail);
        _controller.RollUp(99);

        Assert.Empty(_system.Strips);
        Assert.Equal(2, _system.Warnings);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("DialogOpen", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowWindowsWontMinimizeKeepsNoStripOrMark()
    {
        _system[Mail].MinimizeRefused = true;

        _controller.RollUp(Mail);

        Assert.True(_system.Strips[0].Disposed);
        Assert.Empty(_system.Marks);
        Assert.False(_set.Contains(Mail));
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AWindowThatChangesWhileItsTitleBarIsMeasuredIsCheckedAgain()
    {
        _system.AnswerAtOnce = false;
        _controller.RollUp(Mail);
        _controller.RollUp(Mail);
        _system[Mail].Refusal = ShadeRefusal.FullScreen;

        _system.AnswerMeasurements();

        Assert.Empty(_system.Strips);
        Assert.Single(_system.Calls, call => call.StartsWith("measure", StringComparison.Ordinal));
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AClickOnTheStripRollsTheWindowDownInFront()
    {
        _controller.RollUp(Notes);

        _system.Strips[0].Click(new Point(10, 10));

        Assert.Contains("roll down 2 activate True", _system.Calls);
        Assert.True(_system.Strips[0].Disposed);
        Assert.Empty(_system.Marks);
        Assert.Empty(_system.Watched);
        Assert.False(_set.Contains(Notes));
        Assert.Equal(0, _controller.Count);
    }

    [Fact]
    public void AClickOnThePictureOfTheCloseButtonClosesTheWindow()
    {
        _system[Mail].Buttons = new Rectangle(660, 0, 146, 30);
        _controller.RollUp(Mail);
        var strip = _system.Strips[0];

        // The Close button is the last 46 pixels of the buttons, at 760 to 806 in the window's
        // rectangle, which starts 8 pixels left of the strip: 752 to 798 in the strip.
        strip.Click(new Point(760, 10));

        Assert.Contains("close 1", _system.Calls);
        Assert.True(strip.Disposed);
    }

    [Fact]
    public void RightToLeftTheCloseButtonIsAtTheLeft()
    {
        _system[Mail].Buttons = new Rectangle(7, 0, 146, 30);
        _system[Mail].RightToLeft = true;
        _controller.RollUp(Mail);

        _system.Strips[0].Click(new Point(20, 10));

        Assert.Contains("close 1", _system.Calls);
    }

    [Fact]
    public void TheWheelRollsDownOnlyWhenTurnedDown()
    {
        _controller.RollUp(Mail);

        _system.Strips[0].Wheel(120);
        Assert.Equal(1, _controller.Count);

        _system.Strips[0].Wheel(-120);
        Assert.Equal(0, _controller.Count);
        Assert.Contains("roll down 1 activate True", _system.Calls);
    }

    [Fact]
    public void AWindowTheWheelRolledDownIsntRolledUpAgainForAMoment()
    {
        _controller.RollUp(Mail);
        _system.Strips[0].Wheel(-120);

        _controller.RollUp(Mail);
        Assert.Equal(0, _controller.Count);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("just after the wheel rolled it down", StringComparison.Ordinal));

        _clock.Advance(TimeSpan.FromSeconds(1));
        _controller.RollUp(Mail);
        Assert.Equal(1, _controller.Count);
    }

    [Fact]
    public void TheStripsMenuRollsDownOrCloses()
    {
        _controller.RollUp(Mail);
        _controller.RollUp(Notes);

        _system.MenuChoice = ShadeMenuChoice.RollDown;
        _system.Strips[0].RightClick(new Point(5, 5));
        _system.MenuChoice = ShadeMenuChoice.Close;
        _system.Strips[1].RightClick(new Point(5, 5));

        Assert.Contains("roll down 1 activate True", _system.Calls);
        Assert.Contains("close 2", _system.Calls);
    }

    [Fact]
    public void DraggingTheStripMovesWhereTheWindowRollsDown()
    {
        _controller.RollUp(Mail);
        var strip = _system.Strips[0];

        strip.DragTo(new Point(301, 151));
        strip.DragTo(new Point(311, 151));

        Assert.Contains("move 1 by 200,-50", _system.Calls);
        Assert.Contains("move 1 by 10,0", _system.Calls);
    }

    [Fact]
    public void AWindowRestoredByAnythingElseLosesItsStrip()
    {
        _controller.RollUp(Mail);

        _controller.OnRestored(Mail);

        Assert.True(_system.Strips[0].Disposed);
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("roll down", StringComparison.Ordinal));
        Assert.Equal(0, _controller.Count);
    }

    [Fact]
    public void AWindowThatClosesLosesItsStrip()
    {
        _controller.RollUp(Mail);

        _controller.OnDestroyed(Mail);

        Assert.True(_system.Strips[0].Disposed);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void AWindowItsAppHidesAsItsMinimizedComesBackWithTheWarningSound()
    {
        _controller.RollUp(Mail);
        _controller.OnHidden(Mail);

        Assert.Contains("roll down 1 activate False", _system.Calls);
        Assert.Equal(1, _system.Warnings);

        _controller.RollUp(Mail);
        _clock.Advance(TimeSpan.FromSeconds(3));
        _controller.OnHidden(Mail);

        Assert.Single(_system.Calls, call => call.StartsWith("roll down", StringComparison.Ordinal));
        Assert.Equal(0, _controller.Count);
    }

    [Fact]
    public void RollDownAllAndRollDownLast()
    {
        _controller.RollUp(Mail);
        _controller.RollUp(Notes);

        _controller.RollDownLast();
        Assert.Contains("roll down 2 activate True", _system.Calls);
        Assert.Equal(1, _controller.Count);

        Assert.Equal(1, _controller.RollDownAll());
        Assert.Contains("roll down 1 activate False", _system.Calls);

        _controller.RollDownLast();
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void RollUpFrontRollsUpTheWindowInFront()
    {
        _system.Foreground = Notes;

        _controller.RollUpFront();

        Assert.Equal(Notes, _system.Strips[0].Window);
    }

    [Fact]
    public void ReleasingRollsEveryWindowDownWithoutTheFront()
    {
        _controller.RollUp(Mail);
        _controller.RollUp(Notes);

        Assert.Equal(2, _controller.Release());

        Assert.Contains("roll down 1 activate False", _system.Calls);
        Assert.Contains("roll down 2 activate False", _system.Calls);
        Assert.Empty(_system.Marks);
        Assert.All(_system.Strips, strip => Assert.True(strip.Disposed));
    }

    [Fact]
    public void RecoveringRollsDownMarkedWindowsThatAreStillMinimized()
    {
        _system[Mail].Minimized = true;
        _system.Marks.Add(Mail);
        _system.Marks.Add(Notes);

        Assert.Equal(1, ShadingController.Recover(_system));

        Assert.Contains("roll down 1 activate False", _system.Calls);
        Assert.DoesNotContain("roll down 2 activate False", _system.Calls);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void KeepOnTopAppliesToTheStripsShownNow()
    {
        _controller.RollUp(Mail);

        _controller.Apply(new WindowShadingSettings { Enabled = true, KeepOnTop = false });

        Assert.False(_system.Strips[0].Topmost);
        _controller.RollUp(Notes);
        Assert.False(_system.Strips[1].Topmost);
    }

    [Fact]
    public void HoverRollsDownAfterItsDelayAndUpAgainOnceThePointerHasLeft()
    {
        UseHover();
        _controller.RollUp(Notes);
        var strip = _system.Strips[0];

        strip.Enter();
        Run(TimeSpan.FromMilliseconds(400));
        Assert.True(_system[Notes].Minimized);
        Run(TimeSpan.FromMilliseconds(100));
        Assert.Contains("show 2", _system.Calls);
        Assert.False(strip.Shown);
        _controller.OnRestored(Notes);
        Assert.Equal(1, _controller.Count);

        _system.PointerPosition = new Point(1100, 200);
        Run(TimeSpan.FromMilliseconds(500));
        Assert.False(_system[Notes].Minimized);

        _system.PointerPosition = new Point(10, 10);
        Run(TimeSpan.FromMilliseconds(100));
        Run(TimeSpan.FromMilliseconds(500));
        Assert.False(_system[Notes].Minimized);
        Run(TimeSpan.FromMilliseconds(500));

        Assert.Contains("minimize without activating 2", _system.Calls);
        Assert.True(strip.Shown);
        Assert.Equal(1, _controller.Count);
    }

    [Fact]
    public void LeavingTheStripBeforeTheDelayKeepsTheWindowRolledUp()
    {
        UseHover();
        _controller.RollUp(Notes);
        var strip = _system.Strips[0];

        strip.Enter();
        Run(TimeSpan.FromMilliseconds(300));
        strip.Leave();
        Run(TimeSpan.FromSeconds(1));

        Assert.DoesNotContain("show 2", _system.Calls);
    }

    [Fact]
    public void AWindowHoverShowsStaysDownWhenClickedMovedOrBusy()
    {
        UseHover();
        _controller.RollUp(Notes);
        _system.Strips[0].Enter();
        Run(TimeSpan.FromMilliseconds(500));
        _system.PointerPosition = new Point(10, 10);
        _system[Notes].Busy = true;
        Run(TimeSpan.FromMilliseconds(100));
        Run(TimeSpan.FromSeconds(2));
        Assert.False(_system[Notes].Minimized);

        _controller.OnForeground(Notes);
        Assert.Equal(0, _controller.Count);

        _system[Notes].Busy = false;
        _controller.RollUp(Notes);
        _system.Strips[1].Enter();
        Run(TimeSpan.FromMilliseconds(500));
        Run(TimeSpan.FromMilliseconds(100));
        _system[Notes].Frame = new Rectangle(1200, 300, 600, 400);
        Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, _controller.Count);
    }

    private void UseHover() => _controller.Apply(new WindowShadingSettings { Enabled = true, Hover = new ShadingHover { Enabled = true } });

    // Advances the clock, and runs what its timers posted to the feature's thread.
    private void Run(TimeSpan time)
    {
        _clock.Advance(time);
        while (_thread.TryDequeue(out var action))
        {
            action();
        }
    }
}
