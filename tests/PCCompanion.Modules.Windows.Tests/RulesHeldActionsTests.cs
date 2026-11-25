using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class RulesHeldActionsTests : IDisposable
{
    private const nint Open = 1;
    private const nint New = 2;
    private static readonly Rectangle Start = new(100, 200, 800, 600);
    private static readonly ListedApp Mail = new() { Name = "Mail", Program = @"C:\Apps\Mail.exe" };

    private readonly ManualTimeProvider _clock = new();
    private readonly Queue<Action> _thread = new();
    private readonly FakeRulesSystem _system = new();
    private readonly RulesController _controller;

    public RulesHeldActionsTests()
    {
        _controller = new RulesController(_system, _clock, _thread.Enqueue, new RecordingLogger<RulesController>());
        _system.Add(Open, Start);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public void OnTopOpacityAndTaskbarHidingApplyAsAWindowOpensAndItsMarkedWithThem()
    {
        Use(new WindowRule { App = Mail, OnTop = true, Opacity = new RuleOpacity { Percent = 60 }, HideFromTaskbar = true });

        Opens(New);

        var window = _system[New];
        Assert.True(window.Topmost);
        Assert.False(window.HasButton);
        Assert.Equal(153, window.Attributes!.Value.Alpha);
        Assert.True(window.Layered);
        Assert.Equal(1 | 2 | 4 | 8, (long)_system.Marks[New]);
    }

    [Fact]
    public void WindowsOpenWhenTheRulesStartGetWhatTheRulesHoldButArentPlaced()
    {
        Use(new WindowRule { App = Mail, OnTop = true, Place = new RulePlace { Which = "RightHalf" } });

        Assert.True(_system[Open].Topmost);
        Assert.Equal(Start, _system[Open].Visible);
        Assert.Contains(Open, _controller.HeldWindows);
    }

    [Fact]
    public void ARuleTurnedOnWhileRulesRunGivesTheOpenWindowsItMatchesWhatItHoldsButDoesntPlaceThem()
    {
        var other = new WindowRule { App = new ListedApp { Program = @"C:\Other.exe" }, OnTop = true };
        var mail = new WindowRule { App = Mail, OnTop = true, Opacity = new RuleOpacity { Percent = 60 }, Place = new RulePlace { Which = "RightHalf" } };
        Use(other, mail with { Enabled = false });
        Assert.False(_system[Open].Topmost);

        Use(other, mail);

        Assert.True(_system[Open].Topmost);
        Assert.Equal(153, _system[Open].Attributes!.Value.Alpha);
        Assert.Equal(Start, _system[Open].Visible);
        Assert.Contains(Open, _controller.HeldWindows);
    }

    [Fact]
    public void TurningARuleOffPutsBackWhatItHeld()
    {
        Use(new WindowRule { App = Mail, OnTop = true, HideFromTaskbar = true, Opacity = new RuleOpacity { Percent = 50 } }, new WindowRule { App = new ListedApp { Program = @"C:\Other.exe" }, OnTop = true });

        Use(new WindowRule { App = Mail, OnTop = true, HideFromTaskbar = true, Opacity = new RuleOpacity { Percent = 50 }, Enabled = false }, new WindowRule { App = new ListedApp { Program = @"C:\Other.exe" }, OnTop = true });

        var window = _system[Open];
        Assert.False(window.Topmost);
        Assert.True(window.HasButton);
        Assert.False(window.Layered);
        Assert.Empty(_system.Marks);
        Assert.Empty(_controller.HeldWindows);
    }

    [Fact]
    public void WithoutAUsableRuleEverythingIsPutBack()
    {
        Use(new WindowRule { App = Mail, OnTop = true });

        Use();

        Assert.False(_system[Open].Topmost);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void AWindowItsAppTookOffTheTopIsPutBackWhenItComesToTheFront()
    {
        Use(new WindowRule { App = Mail, OnTop = true });
        _system[Open].Topmost = false;

        _controller.OnForeground(Open);

        Assert.True(_system[Open].Topmost);
    }

    [Fact]
    public void AWindowAlreadyOnTopIsLeftAsItWas()
    {
        _system[Open].Topmost = true;
        Use(new WindowRule { App = Mail, OnTop = true });

        Use(new WindowRule { App = Mail, OnTop = true, Enabled = false }, new WindowRule { App = new ListedApp { Program = @"C:\Other.exe" }, OnTop = true });

        Assert.True(_system[Open].Topmost);
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("topmost", StringComparison.Ordinal));
    }

    [Fact]
    public void AWindowOffTheTaskbarGetsItsButtonBackWhileMinimized()
    {
        Use(new WindowRule { App = Mail, HideFromTaskbar = true });

        _controller.OnMinimizeStart(Open);
        Assert.True(_system[Open].HasButton);

        _controller.OnMinimizeEnd(Open);
        Assert.False(_system[Open].HasButton);
    }

    [Fact]
    public void ButtonsExplorerAddsAgainGoTwoSecondsAfterItRestarts()
    {
        Use(new WindowRule { App = Mail, HideFromTaskbar = true });
        _system[Open].HasButton = true;

        _controller.OnTaskbarCreated();
        Run(TimeSpan.FromSeconds(1));
        Assert.True(_system[Open].HasButton);
        Run(TimeSpan.FromSeconds(1));

        Assert.False(_system[Open].HasButton);
    }

    [Fact]
    public void AWindowThatDrawsItselfKeepsItsOpacityAndOneWithItsOwnGetsItBack()
    {
        _system[Open].Layered = true;
        _system.Add(3, Start).Layered = true;
        _system[3].Attributes = new LayeredAttributes(0x00FF00, 230, LayeredEffects.Alpha | LayeredEffects.ColorKey);
        var rule = new WindowRule { App = Mail, Opacity = new RuleOpacity { Percent = 40 } };
        Use(rule);

        Assert.Null(_system[Open].Attributes);
        Assert.Equal(102, _system[3].Attributes!.Value.Alpha);
        Assert.Equal(0x00FF00u, _system[3].Attributes!.Value.ColorKey);
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("opacity 1", StringComparison.Ordinal));

        Assert.Equal(1, RulesController.Recover(_system));
        Assert.Equal(["put back opacity 3 False"], _system.Calls.Where(call => call.StartsWith("put back", StringComparison.Ordinal)));
    }

    [Fact]
    public void RecoveryPutsBackWhatTheMarkSaysAndRemovesIt()
    {
        _system.Add(3, Start).Topmost = true;
        _system[3].HasButton = false;
        _system.Marks[3] = 1 | 2 | 4 | 8;

        Assert.Equal(1, RulesController.Recover(_system));

        Assert.False(_system[3].Topmost);
        Assert.True(_system[3].HasButton);
        Assert.Contains("put back opacity 3 True", _system.Calls);
        Assert.Empty(_system.Marks);
    }

    [Fact]
    public void ReleaseNowPutsBackEveryWindowAndAClosedOneIsForgotten()
    {
        Use(new WindowRule { App = Mail, OnTop = true, HideFromTaskbar = true });
        Opens(New);
        _controller.OnDestroyed(New);
        _system.Remove(New);

        Assert.Equal(1, _controller.ReleaseNow());

        Assert.False(_system[Open].Topmost);
        Assert.True(_system[Open].HasButton);
        Assert.DoesNotContain("add button 2", _system.Calls);
    }

    [Fact]
    public void ApplyNowHoldsTheRulesActionsToo()
    {
        Use(new WindowRule { App = new ListedApp { Program = @"C:\Other.exe" }, OnTop = true });

        _controller.ApplyNow(new WindowRule { App = Mail, HideFromTaskbar = true });

        Assert.False(_system[Open].HasButton);
    }

    [Fact]
    public void OpenAsATabOfGroupsWithTheFrontMostWindowOfTheAppAndSkipsTheOtherActions()
    {
        _system.Add(3, Start, program: @"C:\Apps\Editor.exe");
        _system.Add(4, Start, program: @"C:\Apps\Editor.exe").Cloaked = true;
        Use(new WindowRule { App = Mail, TabOf = new ListedApp { Program = @"C:\Apps\Editor.exe" }, OnTop = true, Place = new RulePlace { Which = "RightHalf" } });

        Opens(New);

        Assert.Contains("group 2 with 3", _system.Calls);
        Assert.False(_system[New].Topmost);
        Assert.Equal(Start, _system[New].Visible);
    }

    [Fact]
    public void WithoutWindowTabsOrAWindowOfTheAppTheOtherActionsApply()
    {
        Use(new WindowRule { App = Mail, TabOf = new ListedApp { Program = @"C:\Apps\Editor.exe" }, OnTop = true });
        Opens(New);
        Assert.True(_system[New].Topmost);
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("group", StringComparison.Ordinal));

        _system.Add(3, Start, program: @"C:\Apps\Editor.exe");
        _system.TabsOn = false;
        Opens(4);
        Assert.Contains("group 4 with 3", _system.Calls);
        Assert.True(_system[4].Topmost);
    }

    [Fact]
    public void TheRulesOwnAppPutsItsWindowsInOneGroup()
    {
        Use(new WindowRule { App = Mail, TabOf = Mail });

        Opens(New);

        Assert.Contains("group 2 with 1", _system.Calls);
    }

    private void Use(params WindowRule[] rules) => _controller.Apply(new WindowRulesSettings { Enabled = true, Rules = rules });

    private void Opens(nint handle)
    {
        _system.Add(handle, Start);
        _controller.OnShown(handle);
    }

    private void Run(TimeSpan time)
    {
        _clock.Advance(time);
        while (_thread.TryDequeue(out var action))
        {
            action();
        }
    }
}
