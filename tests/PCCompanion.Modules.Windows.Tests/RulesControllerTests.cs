using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class RulesControllerTests : IDisposable
{
    private const nint Open = 1;
    private const nint New = 2;
    private const string MailProgram = @"C:\Apps\Mail.exe";
    private static readonly Rectangle Start = new(100, 200, 800, 600);
    private static readonly Rectangle RightHalf = new(960, 0, 960, 1032);
    private static readonly ListedApp Mail = new() { Name = "Mail", Program = MailProgram };

    private readonly ManualTimeProvider _clock = new();
    private readonly Queue<Action> _thread = new();
    private readonly FakeRulesSystem _system = new();
    private readonly RecordingLogger<RulesController> _logger = new();
    private readonly RulesController _controller;

    public RulesControllerTests()
    {
        _controller = new RulesController(_system, _clock, _thread.Enqueue, _logger);
        _system.Add(Open, Start);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void AWindowThatOpensIsPlacedAtItsRulesPlace()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });

        Opens(New);

        Assert.Equal(RightHalf, _system[New].Visible);
        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("rules 1 to a window: place", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowOpenWhenTheRulesStartIsNotPlacedUntilApplyNow()
    {
        var rule = new WindowRule { App = Mail, Place = Place("RightHalf") };
        Use(rule);

        _controller.OnShown(Open);

        Assert.Equal(Start, _system[Open].Visible);
        Assert.Equal(1, _controller.ApplyNow(rule));
        Assert.Equal(RightHalf, _system[Open].Visible);
    }

    [Fact]
    public void AWindowShownAgainIsNotPlacedAgain()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);
        _system[New].Visible = Start;

        _controller.OnShown(New);

        Assert.Equal(Start, _system[New].Visible);
    }

    [Fact]
    public void AWindowThatShowsCloakedIsPlacedWhenItsUncloaked()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        _system.Add(New, new Rectangle(7, 0, 122, 32)).Cloaked = true;

        _controller.OnShown(New);
        Assert.Empty(_system.Calls);
        _system[New].Cloaked = false;
        _system[New].Visible = Start;
        _controller.OnUncloaked(New);

        Assert.Equal(RightHalf, _system[New].Visible);
    }

    [Fact]
    public void ALateTitleMatchesWithinTenSecondsAndNotAfter()
    {
        Use(new WindowRule { App = Mail, Title = new RuleTitle { Text = "Notes" }, Place = Place("RightHalf") });
        _system.Add(New, Start, title: "Mail");
        _system.Add(3, Start, title: "Mail");
        _controller.OnShown(New);
        _controller.OnShown(3);

        _system[New].Title = "Notes - Mail";
        _controller.OnTitleChanged(New);
        _clock.Advance(TimeSpan.FromSeconds(11));
        _system[3].Title = "Notes - Mail";
        _controller.OnTitleChanged(3);

        Assert.Equal(RightHalf, _system[New].Visible);
        Assert.Equal(Start, _system[3].Visible);
        Assert.DoesNotContain((nint)3, _controller.RuledWindows);
    }

    [Fact]
    public void AnAppThatMovesItsWindowBackIsBeatenAFewTimesWithinTwoSeconds()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);

        for (var time = 0; time < 5; time++)
        {
            _system.AppMoves(New, Start);
            Run(RulesController.SettleCheck);
        }

        Assert.Equal(4, _system.Calls.Count(call => call.StartsWith("place 2", StringComparison.Ordinal)));
        Assert.Equal(Start, _system[New].Visible);
        Assert.False(_controller.IsSettling(New));
        Assert.Equal(0, _system.Followed[New]);
    }

    [Fact]
    public void AWindowTheAppMovesBackOnceIsPlacedAgain()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);

        Run(TimeSpan.FromMilliseconds(400));
        _system.AppMoves(New, Start);
        Run(RulesController.SettleCheck);

        Assert.Equal(RightHalf, _system[New].Visible);
        Run(RulesController.SettleTime);
        Assert.False(_controller.IsSettling(New));
    }

    [Fact]
    public void AWindowTheUserMovesIsNotPlacedAgain()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);

        _controller.OnMoveSizeStart(New);
        _system.AppMoves(New, Start);
        Run(RulesController.SettleCheck);

        Assert.Equal(Start, _system[New].Visible);
    }

    [Fact]
    public void AMoveAfterTwoSecondsIsLeftAlone()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);

        Run(RulesController.SettleTime + TimeSpan.FromMilliseconds(100));
        _system.AppMoves(New, Start);
        Run(RulesController.SettleCheck);

        Assert.Equal(Start, _system[New].Visible);
    }

    [Fact]
    public void AChosenDisplayPutsTheWindowThereAndOneThatIsNotConnectedLeavesItWhereItIs()
    {
        Use(new WindowRule { App = Mail, Display = new RuleDisplay { Which = "Chosen", Model = "SECOND" }, Place = Place("LeftHalf") });
        Opens(New);
        Assert.Equal(new Rectangle(1920, 0, 1280, 1392), _system[New].Visible);

        _system.Connected.Clear();
        Opens(3);
        Assert.Equal(new Rectangle(0, 0, 960, 1032), _system[3].Visible);
    }

    [Fact]
    public void ADisplayWithoutAPlaceKeepsTheWindowsSize()
    {
        Use(new WindowRule { App = Mail, Display = new RuleDisplay { Which = "UnderPointer" } });

        Opens(New);

        Assert.Equal(new Rectangle(2053, 270, 800, 600), _system[New].Visible);
    }

    [Fact]
    public void OpenMaximizedMaximizesWithTheRulesPlaceAsItsNormalPlace()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf"), Open = "Maximized" });

        Opens(New);

        Assert.Equal([$"placement 2 {FakeRulesSystem.Inflate(RightHalf)} Maximized"], _system.Calls);
        Assert.False(_controller.IsSettling(New));
    }

    [Fact]
    public void OpenMinimizedAndNormal()
    {
        Use(new WindowRule { App = Mail, Title = new RuleTitle { Text = "Big" }, Open = "Normal", Place = Place("RightHalf") }, new WindowRule { App = Mail, Open = "Minimized" });
        Opens(New);
        Assert.Equal(WindowState.Minimized, _system[New].State);

        _system.Add(3, Start, title: "Big").State = WindowState.Maximized;
        _controller.OnShown(3);
        Assert.Equal(WindowState.Normal, _system[3].State);
        Assert.Equal(RightHalf, _system[3].Visible);
    }

    [Fact]
    public void AWindowThatOpensMaximizedStaysMaximizedWithTheRulesPlaceAsItsNormalPlace()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        _system.Add(New, Start).State = WindowState.Maximized;

        _controller.OnShown(New);

        Assert.Equal([$"placement 2 {FakeRulesSystem.Inflate(RightHalf)} Maximized"], _system.Calls);
    }

    [Theory]
    [InlineData(RuleRefusal.RunsAsAdministrator)]
    [InlineData(RuleRefusal.OtherFeature)]
    [InlineData(RuleRefusal.NotMain)]
    public void WindowsRulesLeaveAloneAreNotPlaced(RuleRefusal refusal)
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        _system.Add(New, Start).Refusal = refusal;

        _controller.OnShown(New);

        Assert.Empty(_system.Calls);
        Assert.Equal(refusal == RuleRefusal.RunsAsAdministrator, _logger.Entries.Any(entry => entry.Message.Contains("administrator", StringComparison.Ordinal)));
    }

    [Fact]
    public void ApplyNowSkipsSnappedAndFullScreenWindowsAndKeepsAMinimizedOneMinimized()
    {
        _system.Add(3, Start).Arranged = true;
        _system.Add(4, Start).FullScreen = true;
        _system.Add(5, Start).State = WindowState.Minimized;
        var rule = new WindowRule { App = Mail, Place = Place("RightHalf") };
        Use(rule);

        Assert.Equal(4, _controller.ApplyNow(rule));

        Assert.Equal(RightHalf, _system[Open].Visible);
        Assert.Equal(Start, _system[3].Visible);
        Assert.Equal(Start, _system[4].Visible);
        Assert.Equal(WindowState.Minimized, _system[5].State);
        Assert.Equal(FakeRulesSystem.Inflate(RightHalf), _system[5].Normal);
    }

    [Fact]
    public void ApplyNowAppliesOnlyThatRuleAndOnlyAUsableOne()
    {
        Use(new WindowRule { App = Mail, Place = Place("LeftHalf") });

        _controller.ApplyNow(new WindowRule { App = Mail, Place = Place("RightHalf") });

        Assert.Equal(RightHalf, _system[Open].Visible);
        Assert.Equal(0, _controller.ApplyNow(new WindowRule { App = Mail }));
        Assert.Equal(0, _controller.ApplyNow(new WindowRule { App = Mail, Place = Place("RightHalf"), Enabled = false }));
    }

    [Fact]
    public void ChangingTheRulesDoesNotPlaceOpenWindows()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);
        Run(RulesController.SettleTime);
        _system[New].Visible = Start;

        Use(new WindowRule { App = Mail, Place = Place("LeftHalf") });

        Assert.Equal(Start, _system[New].Visible);
        Assert.Contains(New, _controller.RuledWindows);
    }

    [Fact]
    public void AHandleUsedAgainAfterItsWindowClosedIsANewWindow()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        Opens(New);
        _controller.OnDestroyed(New);
        _system.Remove(New);

        Opens(New);

        Assert.Equal(2, _system.Calls.Count);
    }

    [Fact]
    public void WithoutAUsableRuleNothingIsWatchedAndTheWindowsOpenWhenRulesComeBackAreNotNew()
    {
        Use(new WindowRule { App = Mail, Place = Place("RightHalf"), Enabled = false });
        Assert.False(_controller.IsWatching);
        _system.Add(New, Start);

        Use(new WindowRule { App = Mail, Place = Place("RightHalf") });
        _controller.OnShown(New);

        Assert.True(_controller.IsWatching);
        Assert.Empty(_system.Calls);
    }

    [Fact]
    public void AfterTheDisplaysChangeTheWindowsOfRulesThatSaySoArePlacedAgain()
    {
        Use(
            new WindowRule { App = Mail, Title = new RuleTitle { Text = "Again" }, Place = Place("RightHalf"), ApplyOnDisplayChange = true },
            new WindowRule { App = Mail, Place = Place("LeftHalf") });
        _system.Add(New, Start, title: "Again");
        _system.Add(3, Start, title: "Again").Arranged = true;
        _system.Add(4, Start, title: "Once");
        foreach (var window in new nint[] { New, 3, 4 })
        {
            _controller.OnShown(window);
            _controller.OnMoveSizeStart(window);
            _system[window].Visible = Start;
        }

        _controller.OnDisplaysChanged();
        Run(TimeSpan.FromSeconds(1));
        _controller.OnDisplaysChanged();
        Run(TimeSpan.FromSeconds(1));
        Assert.Equal(Start, _system[New].Visible);
        Run(TimeSpan.FromSeconds(1));

        Assert.Equal(RightHalf, _system[New].Visible);
        Assert.Equal(Start, _system[3].Visible);
        Assert.Equal(Start, _system[4].Visible);
    }

    [Fact]
    public void WhereIlastPutItOpensTheNextWindowThereOnItsDisplay()
    {
        var rule = new WindowRule { Id = "mail", App = Mail, Place = Place("Remembered") };
        Use(rule);
        Opens(New);
        Assert.Equal(Start, _system[New].Visible);

        _system[New].Visible = new Rectangle(2000, 100, 1000, 700);
        _controller.OnMoveSizeEnd(New);
        _controller.OnHidden(New);
        _controller.OnDestroyed(New);
        _system.Remove(New);
        Opens(3);

        Assert.Equal(new Rectangle(2000, 100, 1000, 700), _system[3].Visible);
        Assert.NotNull(_controller.Remembered.Find("mail", _system.DisplaySet));
    }

    [Fact]
    public void AWindowWhoseFrameIsGoneAsItClosesIsRememberedFromItsPlacement()
    {
        Use(new WindowRule { Id = "mail", App = Mail, Place = Place("Remembered") });
        Opens(New);
        _system[New].Normal = FakeRulesSystem.Inflate(new Rectangle(300, 200, 800, 600));
        _system[New].FrameGone = true;

        _controller.OnHidden(New);
        Opens(3);

        Assert.Equal(new Rectangle(300, 200, 800, 600), _system[3].Visible);
    }

    [Fact]
    public void AWindowThatWouldOpenOverAnotherOfTheRuleMovesDownAndRight()
    {
        Use(new WindowRule { Id = "mail", App = Mail, Place = Place("Remembered") });
        Opens(New);
        _system[New].Visible = new Rectangle(200, 100, 800, 600);
        _controller.OnMoveSizeEnd(New);

        Opens(3);
        Opens(4);

        Assert.Equal(new Rectangle(232, 132, 800, 600), _system[3].Visible);
        Assert.Equal(new Rectangle(264, 164, 800, 600), _system[4].Visible);
    }

    [Fact]
    public void ARememberedMaximizedWindowOpensMaximizedAndOneOnADisplayThatsGoneOnTheMainOne()
    {
        Use(new WindowRule { Id = "mail", App = Mail, Place = Place("Remembered") });
        Opens(New);
        _system[New].State = WindowState.Maximized;
        _system[New].Normal = FakeRulesSystem.Inflate(new Rectangle(2100, 100, 1000, 700));
        _controller.OnHidden(New);

        Opens(3);
        Assert.Equal(WindowState.Maximized, _system[3].State);
        Assert.Equal(FakeRulesSystem.Inflate(new Rectangle(2100, 100, 1000, 700)), _system[3].Normal);

        _controller.Remembered.Remember("mail", _system.DisplaySet, new RuleDisplay { Which = "Chosen", Model = "GONE" }, new Rectangle(2000, 0, 1280, 696), FakeRulesSystem.SecondArea, maximized: false);
        Opens(4);
        Assert.Equal(new Rectangle(60, 0, 960, 516), _system[4].Visible);
    }

    [Fact]
    public void TheWheelsOpacityIsKeptForARuleThatSaysSoAndGivenToItsNextWindows()
    {
        Use(new WindowRule { Id = "mail", App = Mail, Opacity = new RuleOpacity { Which = "Wheel" } });
        Opens(New);
        Assert.Null(_system[New].Attributes);

        _controller.OnWheelOpacity(New, 60);
        _controller.OnWheelOpacity(99, 30);
        Opens(3);

        Assert.Equal(60, _controller.Remembered.OpacityOf("mail"));
        Assert.Equal(153, _system[3].Attributes!.Value.Alpha);
    }

    [Fact]
    public void WhatDeletedRulesRememberedIsForgotten()
    {
        Use(new WindowRule { Id = "mail", App = Mail, Opacity = new RuleOpacity { Which = "Wheel" } });
        _controller.Remembered.RememberOpacity("mail", 50);

        Use(new WindowRule { Id = "mail", App = Mail, Opacity = new RuleOpacity { Which = "Wheel" }, Enabled = false }, new WindowRule { App = Mail, OnTop = true });
        Assert.Equal(50, _controller.Remembered.OpacityOf("mail"));

        Use(new WindowRule { App = Mail, OnTop = true });
        Assert.Null(_controller.Remembered.OpacityOf("mail"));
    }

    [Fact]
    public void RulesThatCantBeUsedAreLogged()
    {
        Use(new WindowRule { App = Mail }, new WindowRule { App = Mail, OnTop = true });

        Assert.Contains(_logger.Entries, entry => entry.Message.Contains("rule 1 can't be used: NoAction", StringComparison.Ordinal));
    }

    private static RulePlace Place(string which) => new() { Which = which };

    private void Use(params WindowRule[] rules) => _controller.Apply(new WindowRulesSettings { Enabled = true, Rules = rules });

    private void Opens(nint handle)
    {
        if (!_system.TaskbarWindows().Contains(handle))
        {
            _system.Add(handle, Start);
        }

        _controller.OnShown(handle);
    }

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
