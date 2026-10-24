using PCCompanion.Modules.Windows.AppWindows;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Modules.Windows.Tests.FakeAppWindowsEnvironment;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowSwitcherTests
{
    private const string One = @"C:\Apps\one.exe";
    private const string Two = @"C:\Apps\two.exe";

    private readonly FakeAppWindowsEnvironment _environment = new();
    private readonly WindowSwitcher _switcher;

    public WindowSwitcherTests()
    {
        _switcher = new WindowSwitcher(_environment);
        _environment.Paths[1] = One;
        _environment.Paths[2] = Two;
    }

    [Fact]
    public void ForwardBringsTheAppsWindowFurthestBackToTheFront()
    {
        ThreeWindowsOfOneAppAndAnother();

        var result = _switcher.Switch(SwitchDirection.Forward, includeMinimized: false);

        Assert.Equal(new SwitchResult(SwitchOutcome.Switched, 3), result);
        Assert.Equal([3, 1, 2], _environment.Order(1, 2, 3));
        Assert.Equal(["activate 3"], _environment.Calls);
    }

    [Fact]
    public void RepeatedPressesForwardVisitEveryWindowAndComeBackToTheFirst()
    {
        ThreeWindowsOfOneAppAndAnother();

        nint[] fronts = [Forward(), Forward(), Forward()];

        Assert.Equal([3, 2, 1], fronts);
        Assert.Equal([1, 2, 3], _environment.Order(1, 2, 3));
    }

    [Fact]
    public void BackBringsTheSecondWindowToTheFrontAndSendsTheFrontOneBack()
    {
        ThreeWindowsOfOneAppAndAnother();

        var result = _switcher.Switch(SwitchDirection.Back, includeMinimized: false);

        Assert.Equal(new SwitchResult(SwitchOutcome.Switched, 3), result);
        Assert.Equal([2, 3, 1], _environment.Order(1, 2, 3));
        Assert.Equal(["activate 2", "send 1 to the back"], _environment.Calls);
    }

    [Fact]
    public void RepeatedPressesBackVisitEveryWindowAndComeBackToTheFirst()
    {
        ThreeWindowsOfOneAppAndAnother();

        nint[] fronts = [Back(), Back(), Back()];

        Assert.Equal([2, 3, 1], fronts);
        Assert.Equal([1, 2, 3], _environment.Order(1, 2, 3));
    }

    [Fact]
    public void ForwardThenBackLeavesTheWindowsInTheirOrder()
    {
        ThreeWindowsOfOneAppAndAnother();

        Forward();
        Back();

        Assert.Equal([1, 2, 3], _environment.Order(1, 2, 3));
    }

    [Theory]
    [InlineData("cloaked")]
    [InlineData("tool")]
    [InlineData("owned")]
    [InlineData("noActivate")]
    [InlineData("minimized")]
    public void WindowsThatAltTabDoesNotShowAreSkipped(string kind)
    {
        Add(Window(1, 1), Window(2, 1), Kind(Window(3, 1), kind));

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void AnOwnedToolWindowThatAsksToBeOnTheTaskbarCounts()
    {
        Add(Window(1, 1), Window(2, 1), Window(3, 1, tool: true, owned: true, appWindow: true));

        Assert.Equal(3, Forward());
    }

    [Fact]
    public void MinimizedWindowsCountWhenIncludedAndAreRestored()
    {
        Add(Window(1, 1), Window(2, 1), Window(3, 1, minimized: true));

        _switcher.Switch(SwitchDirection.Forward, includeMinimized: true);

        Assert.Equal(3, _environment.ForegroundWindow);
        Assert.False(_environment.Windows[0].IsMinimized);
    }

    [Fact]
    public void WithADialogInFrontItsAppsWindowsSwitch()
    {
        Add(Window(9, 1, owned: true), Window(1, 1), Window(2, 1));
        _environment.Owners[9] = 1;
        _environment.ForegroundWindow = 9;

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void WithOneWindowNothingHappens()
    {
        Add(Window(1, 1), Window(10, 2));

        Assert.Equal(new SwitchResult(SwitchOutcome.OneWindow, 1), _switcher.Switch(SwitchDirection.Forward, includeMinimized: false));
        Assert.Empty(_environment.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(21)]
    public void WithoutAnAppInFrontNothingHappens(int front)
    {
        Add(Window(1, 1), Window(2, 1), Window(20, 3, desktop: true), Window(21, 3, tool: true));
        _environment.ForegroundWindow = front;

        Assert.Equal(new SwitchResult(SwitchOutcome.NoAppInFront, 0), _switcher.Switch(SwitchDirection.Forward, includeMinimized: false));
        Assert.Empty(_environment.Calls);
    }

    [Fact]
    public void AnAlwaysOnTopWindowInFrontIsNotSentBack()
    {
        Add(Window(1, 1, topmost: true), Window(2, 1), Window(3, 1));

        Back();

        Assert.Equal(["activate 2"], _environment.Calls);
    }

    [Fact]
    public void WindowsRefusingIsReported()
    {
        Add(Window(1, 1), Window(2, 1));
        _environment.RefusesActivation = true;

        Assert.Equal(SwitchOutcome.Refused, _switcher.Switch(SwitchDirection.Forward, includeMinimized: false).Outcome);
    }

    [Fact]
    public void WhenWindowsRefusesGoingBackTheWindowInFrontStaysWhereItIs()
    {
        ThreeWindowsOfOneAppAndAnother();
        _environment.RefusesActivation = true;

        Assert.Equal(SwitchOutcome.Refused, _switcher.Switch(SwitchDirection.Back, includeMinimized: false).Outcome);
        Assert.Equal(["activate 2"], _environment.Calls);
        Assert.Equal([1, 10, 2, 3], _environment.Order(1, 10, 2, 3));
    }

    [Fact]
    public void WindowsOfOneProgramInSeparateProcessesAreOneApp()
    {
        _environment.Paths[3] = One;
        Add(Window(1, 1), Window(2, 3));

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void AnAppIdAWindowSetsSeparatesItFromTheRestOfItsProcess()
    {
        _environment.WindowIds[3] = "Browser.Profile2";
        Add(Window(1, 1), Window(2, 1), Window(3, 1));

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void WindowsWithTheSameAppIdAreOneAppWhateverTheirProgram()
    {
        _environment.WindowIds[1] = "Company.App";
        _environment.WindowIds[2] = "Company.App";
        Add(Window(1, 1), Window(10, 1), Window(2, 2));

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void StoreAppsAreGroupedByTheirPackageNotTheirProgram()
    {
        _environment.Packages[1] = "Store.App_1!App";
        _environment.Packages[3] = "Store.Other_1!App";
        _environment.Paths[3] = One;
        Add(Window(1, 1), Window(3, 3), Window(2, 1));

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void ProcessesWhoseProgramCanNotBeReadAreNotGroupedTogether()
    {
        _environment.Paths.Remove(1);
        Add(Window(1, 1), Window(3, 3), Window(2, 1));

        Assert.Equal(2, Forward());
    }

    [Fact]
    public void EachProcessIsReadOncePerPress()
    {
        Add(Window(1, 1), Window(2, 1), Window(3, 1), Window(10, 2));

        Forward();

        Assert.Equal(2, _environment.ProcessReads);
    }

    private static TopLevelWindow Kind(TopLevelWindow window, string kind) => kind switch
    {
        "cloaked" => window with { IsCloaked = true },
        "tool" => window with { IsToolWindow = true },
        "owned" => window with { HasOwner = true },
        "noActivate" => window with { IsNoActivate = true },
        "minimized" => window with { IsMinimized = true },
        _ => window,
    };

    // Windows 1, 2 and 3 of one app, with another app's window between the first two.
    private void ThreeWindowsOfOneAppAndAnother() => Add(Window(1, 1), Window(10, 2), Window(2, 1), Window(3, 1));

    // Adds the windows front to back, with the first in front.
    private void Add(params TopLevelWindow[] windows)
    {
        _environment.Windows.AddRange(windows);
        _environment.ForegroundWindow = windows[0].Handle;
    }

    private nint Forward()
    {
        _switcher.Switch(SwitchDirection.Forward, includeMinimized: false);
        return _environment.ForegroundWindow;
    }

    private nint Back()
    {
        _switcher.Switch(SwitchDirection.Back, includeMinimized: false);
        return _environment.ForegroundWindow;
    }
}
