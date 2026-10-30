using System;
using System.Drawing;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class HideAppsControllerTests : IDisposable
{
    private const string AppA = @"C:\Apps\A.exe";
    private const string AppB = @"C:\Apps\B.exe";
    private const string AppC = @"C:\Apps\C.exe";

    private readonly FakeHideAppsSystem _system = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly HideAppsController _hide;

    public HideAppsControllerTests()
    {
        _hide = new HideAppsController(_system, _clock, action => action(), new RecordingLogger<HideAppsController>());
        _hide.Apply(new HideAppsSettings { Enabled = true });
    }

    public void Dispose() => _hide.Dispose();

    [Fact]
    public void HideAppMinimizesEveryWindowOfTheAppInFrontTheWindowInFrontLast()
    {
        A(1);
        B(2);
        A(3);
        C(4);
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.Hid, 2), _hide.HideApp());

        Assert.Equal(["minimize quietly 3", "minimize 1"], _system.Calls.FindAll(call => call.StartsWith("minimize", StringComparison.Ordinal)));
        Assert.True(_system[1].Minimized && _system[3].Minimized);
        Assert.False(_system[2].Minimized || _system[4].Minimized);
        Assert.Equal(2, _system.ForegroundWindow);
        Assert.Equal(2, _hide.Count);
        Assert.Equal([10u], _system.Watched);
        Assert.True(_system.WatchesRestores);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void HideAppActsOnTheAppOfTheWindowThatOwnsAPaletteInFront()
    {
        A(1);
        B(2);
        _system.Order.Add(new FakeHideWindow { Handle = 5, ProcessId = 10, Program = AppA, Owner = 1, ToolWindow = true });
        _system.ForegroundWindow = 5;

        Assert.Equal(new PressResult(PressOutcome.Hid, 1), _hide.HideApp());

        Assert.True(_system[1].Minimized);
        Assert.DoesNotContain("minimize 5", _system.Calls);
    }

    [Fact]
    public void HideAppMinimizesTheAppsAlwaysOnTopWindowsToo()
    {
        A(1);
        A(2).Topmost = true;
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.Hid, 2), _hide.HideApp());

        Assert.True(_system[2].Minimized);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("desktop")]
    [InlineData("ghost")]
    public void WithNoAppInFrontBothShortcutsPlayTheWarning(string front)
    {
        _system.Order.Add(new FakeHideWindow { Handle = 1, ProcessId = 10, Program = AppA, Desktop = front == "desktop", ClassName = front == "ghost" ? "Ghost" : "AppWindow" });
        B(2);
        _system.ForegroundWindow = front == "nothing" ? 0 : 1;

        Assert.Equal(new PressResult(PressOutcome.NoAppInFront), _hide.HideApp());
        Assert.Equal(new PressResult(PressOutcome.NoAppInFront), _hide.HideOthers());

        Assert.False(_system[2].Minimized);
        Assert.Equal(2, _system.Warnings);
    }

    [Fact]
    public void HideOthersMinimizesEveryOtherAppsWindowsWithoutTakingTheFront()
    {
        A(1);
        B(2);
        A(3);
        C(4);
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.Hid, 2), _hide.HideOthers());

        Assert.Equal(["minimize quietly 2", "minimize quietly 4"], _system.Calls.FindAll(call => call.StartsWith("minimize", StringComparison.Ordinal)));
        Assert.False(_system[1].Minimized || _system[3].Minimized);
        Assert.Equal(1, _system.ForegroundWindow);
        Assert.Equal([20u, 30u], _system.Watched);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void HideOthersLeavesWindowsThatStayAndWarnsOnlyForADialog()
    {
        A(1);
        B(2).Topmost = true;
        B(3).MinimizeButton = false;
        B(4).Cloaked = true;
        B(5).Minimized = true;
        _system.Order.Add(new FakeHideWindow { Handle = 6, ProcessId = 99, Program = @"C:\Windows\System32\dwm.exe", ClassName = "Ghost" });
        C(7).DialogOpen = true;
        C(8);
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.Hid, 1, Warned: 1), _hide.HideOthers());

        Assert.Equal(["minimize quietly 8"], _system.Calls.FindAll(call => call.StartsWith("minimize", StringComparison.Ordinal)));
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void HideOthersCanStayOnTheDisplayInFront()
    {
        A(1);
        B(2).Display = new Rectangle(1920, 0, 1920, 1080);
        B(3);
        _system.ForegroundWindow = 1;
        _hide.Apply(new HideAppsSettings { Enabled = true, FrontDisplayOnly = true });

        Assert.Equal(new PressResult(PressOutcome.Hid, 1), _hide.HideOthers());

        Assert.False(_system[2].Minimized);
        Assert.True(_system[3].Minimized);
    }

    [Fact]
    public void HideOthersKeepsAnAppWhateverWayItsAppIdIsRead()
    {
        _system.Order.Add(new FakeHideWindow { Handle = 1, ProcessId = 10, Program = AppA, AppId = "Browser.Work" });
        _system.Order.Add(new FakeHideWindow { Handle = 2, ProcessId = 40, Program = AppB, AppId = "browser.work" });
        _system.Order.Add(new FakeHideWindow { Handle = 3, ProcessId = 10, Program = AppA, AppId = "Browser.Home" });
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.Hid, 1), _hide.HideOthers());

        Assert.False(_system[2].Minimized);
        Assert.True(_system[3].Minimized);
    }

    [Fact]
    public void HideOthersWithNothingToMinimizePlaysTheWarning()
    {
        A(1);
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.NothingToDo), _hide.HideOthers());

        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AnAppRunningAsAdministratorIsAskedToMinimize()
    {
        A(1);
        B(2).RunsAsAdministrator = true;
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.Hid, 1), _hide.HideOthers());

        Assert.Equal(["minimize quietly 2", "ask minimize 2"], _system.Calls.FindAll(call => call.Contains("minimize", StringComparison.Ordinal)));
        Assert.True(_system[2].Minimized);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void AWindowThatWindowsRefusesEvenToAskStaysWithTheWarning()
    {
        A(1);
        var refused = B(2);
        refused.RunsAsAdministrator = true;
        refused.RefusesRequests = true;
        _system.ForegroundWindow = 1;

        Assert.Equal(new PressResult(PressOutcome.NothingToDo, 0, Warned: 1), _hide.HideOthers());

        Assert.Equal(1, _system.Warnings);
        Assert.Equal(0, _hide.Count);
    }

    [Fact]
    public void PressingHideOthersAgainBringsTheWindowsBackInOrderBehindTheApp()
    {
        A(1);
        B(2);
        A(3);
        C(4);
        B(5);
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();
        _system.Calls.Clear();

        Assert.Equal(new PressResult(PressOutcome.BroughtBack, 3), _hide.HideOthers());

        Assert.Equal(["restore 2", "restore 4", "restore 5", "watch order True", "behind 2 3", "behind 4 2", "behind 5 4", "watch order False"], _system.Calls.FindAll(call => !call.StartsWith("unwatch", StringComparison.Ordinal) && !call.StartsWith("watch restores", StringComparison.Ordinal)));
        Assert.Equal([1, 3, 2, 4, 5], _system.Handles);
        Assert.Equal(0, _hide.Count);
        Assert.Empty(_system.Watched);
        Assert.False(_system.WatchesRestores);
        Assert.Equal(1, _system.ForegroundWindow);
    }

    [Fact]
    public void WithAnotherAppInFrontHideOthersHidesItsOthersAndKeepsTheEarlierOnesForTheNextPress()
    {
        A(1);
        B(2);
        C(3);
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();

        // The user brings back B's window, from its taskbar button.
        _system[2].Minimized = false;
        _system.ForegroundWindow = 2;
        _hide.OnRestored(2);

        Assert.Equal(new PressResult(PressOutcome.Hid, 1), _hide.HideOthers());
        Assert.True(_system[1].Minimized);
        Assert.Equal(2, _hide.Count);

        Assert.Equal(new PressResult(PressOutcome.BroughtBack, 2), _hide.HideOthers());
        Assert.Equal([2, 1, 3], _system.Handles);
        Assert.False(_system[1].Minimized || _system[3].Minimized);
    }

    [Fact]
    public void ShowAllBringsBackTheWindowsOfBothShortcuts()
    {
        A(1);
        B(2);
        C(3);
        _system.ForegroundWindow = 2;
        _ = _hide.HideApp();
        Assert.Equal(1, _system.ForegroundWindow);
        _ = _hide.HideOthers();

        Assert.Equal(new PressResult(PressOutcome.BroughtBack, 2), _hide.ShowAll());

        Assert.Equal([1, 3, 2], _system.Handles);
        Assert.False(_system[2].Minimized || _system[3].Minimized);
        Assert.Equal(0, _hide.Count);
    }

    [Fact]
    public void ShowAllWithoutAnAppInFrontBringsTheWindowsBackWhereTheyAre()
    {
        A(1);
        B(2);
        C(3);
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();
        _system.ForegroundWindow = 0;
        _system.Calls.Clear();

        Assert.Equal(new PressResult(PressOutcome.BroughtBack, 2), _hide.ShowAll());

        Assert.Contains("behind 3 2", _system.Calls);
        Assert.DoesNotContain("behind 2 1", _system.Calls);
    }

    [Fact]
    public void ShowAllWithNothingToBringBackPlaysTheWarning()
    {
        Assert.Equal(new PressResult(PressOutcome.NothingToDo), _hide.ShowAll());

        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AWindowComingBackBringsBackItsAppsOtherWindowsBehindIt()
    {
        A(1);
        B(2);
        B(3);
        C(4);
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();

        _system[3].Minimized = false;
        _system.ForegroundWindow = 3;
        _hide.OnRestored(3);

        Assert.False(_system[2].Minimized);
        Assert.Contains("behind 2 3", _system.Calls);
        Assert.True(_system[4].Minimized);
        Assert.Equal(1, _hide.Count);
    }

    [Fact]
    public void WithTheSwitchOffOnlyTheWindowThatCameBackComesBack()
    {
        A(1);
        B(2);
        B(3);
        _system.ForegroundWindow = 1;
        _hide.Apply(new HideAppsSettings { Enabled = true, BringBackWholeApp = false });
        _ = _hide.HideOthers();

        _system[3].Minimized = false;
        _hide.OnRestored(3);

        Assert.True(_system[2].Minimized);
        Assert.Equal(1, _hide.Count);
    }

    [Fact]
    public void AWindowThatComesBackOnItsOwnLeavesTheListAndOthersArentAffected()
    {
        A(1);
        B(2);
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();

        _hide.OnRestored(99);
        Assert.Equal(1, _hide.Count);

        _hide.OnRestored(2);
        Assert.Equal(0, _hide.Count);
        Assert.False(_system.WatchesRestores);
    }

    [Fact]
    public void AnAppRunningAsAdministratorIsAskedToComeBackAndTheFrontGoesBack()
    {
        A(1);
        B(2).RunsAsAdministrator = true;
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();
        _system.Calls.Clear();

        Assert.Equal(new PressResult(PressOutcome.BroughtBack, 1), _hide.HideOthers());
        Assert.Equal(2, _system.ForegroundWindow);

        _clock.Advance(TimeSpan.FromMilliseconds(50));

        Assert.Equal(1, _system.ForegroundWindow);
        Assert.Equal(["ask restore 2", "activate 1"], _system.Calls.FindAll(call => call.StartsWith("ask", StringComparison.Ordinal) || call.StartsWith("activate", StringComparison.Ordinal)));
        Assert.DoesNotContain("restore 2", _system.Calls);
        Assert.DoesNotContain("behind 2 1", _system.Calls);
    }

    [Fact]
    public void AWindowThatClosesLeavesTheList()
    {
        A(1);
        B(2);
        C(3);
        _system.ForegroundWindow = 1;
        _ = _hide.HideOthers();

        _hide.OnDestroyed(2);

        Assert.Equal(1, _hide.Count);
        Assert.Equal([30u], _system.Watched);
        Assert.Contains("unwatch 20", _system.Calls);
    }

    private FakeHideWindow A(nint handle) => _system.Add(handle, 10, AppA);

    private FakeHideWindow B(nint handle) => _system.Add(handle, 20, AppB);

    private FakeHideWindow C(nint handle) => _system.Add(handle, 30, AppC);
}
