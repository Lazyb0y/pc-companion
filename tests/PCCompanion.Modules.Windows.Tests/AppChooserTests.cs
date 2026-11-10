using System;
using System.Drawing;
using PCCompanion.Modules.Windows.EndApp;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class AppChooserTests
{
    private static readonly Rectangle Left = new(0, 0, 400, 300);
    private static readonly Rectangle Right = new(500, 0, 400, 300);
    private static readonly Point InLeft = new(100, 100);
    private static readonly Point InRight = new(600, 100);

    private readonly FakeEndAppSystem _system = new();
    private readonly AppChooser _chooser;

    public AppChooserTests() => _chooser = new AppChooser(_system);

    [Fact]
    public void TheWindowUnderThePointerIsTheFrontMostThatHoldsIt()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 20, Left);
        _system.AddWindow(3, 30, Right);

        Assert.Equal(1, _chooser.WindowAt(InLeft, _system.Windows));
        Assert.Equal(3, _chooser.WindowAt(InRight, _system.Windows));
        Assert.Equal(0, _chooser.WindowAt(new Point(450, 100), _system.Windows));
    }

    [Fact]
    public void PcCompanionsOwnCloakedAndClickThroughWindowsAreSkipped()
    {
        _system.Windows.Add(new EndAppWindow(1, FakeEndAppSystem.Host, Left, Left, false, false, false, false));
        _system.Windows.Add(new EndAppWindow(2, 20, Left, Left, IsCloaked: true, false, true, false));
        _system.Windows.Add(new EndAppWindow(3, 30, Left, Left, false, IsClickThrough: true, false, false));
        _system.AddWindow(4, 40, Left);

        Assert.Equal(4, _chooser.WindowAt(InLeft, _system.Windows));
    }

    [Fact]
    public void TheVisibleFrameCountsNotTheInvisibleBorders()
    {
        _system.Windows.Add(new EndAppWindow(1, 10, new Rectangle(-7, 0, 414, 307), Left, false, false, true, false));

        Assert.Equal(0, _chooser.WindowAt(new Point(-3, 100), _system.Windows));
        Assert.Equal(1, _chooser.WindowAt(new Point(3, 100), _system.Windows));
    }

    [Fact]
    public void TabVisitsTheWindowsTheTaskbarShowsFrontToBack()
    {
        _system.AddWindow(1, 10, Left);
        _system.AddWindow(2, 10, Left, taskbar: false);
        _system.AddWindow(3, 30, Right);
        _system.Windows.Add(new EndAppWindow(4, FakeEndAppSystem.Host, Right, Right, false, false, true, false));
        _system.Windows.Add(new EndAppWindow(5, 50, Right, Right, IsCloaked: true, false, true, false));

        Assert.Equal([1, 3], _chooser.TabOrder(_system.Windows));
    }

    [Fact]
    public void TabVisitsAStandInInsteadOfTheWindowItStandsFor()
    {
        _system.AddWindow(1, 90, Left, @"C:\Windows\System32\dwm.exe", className: "Ghost");
        _system.AddWindow(2, 20, Left);
        _system.AddWindow(3, 30, Right);

        Assert.Equal([1, 3], _chooser.TabOrder(_system.Windows));
    }

    [Fact]
    public void ADialogsAppIsItsMainWindows()
    {
        _system.AddWindow(1, 10, Left, taskbar: false);
        _system.AddWindow(2, 10, Left);
        _system.Owners[1] = 2;

        var chosen = _chooser.Describe(1);

        Assert.Equal(new ChosenApp(2, 2, 10, EndRefusal.None), chosen);
        Assert.True(chosen.CanEnd);
    }

    [Fact]
    public void AStandInStandsForTheWindowThatStoppedRespondingWithItsBounds()
    {
        _system.AddWindow(1, 90, Left, @"C:\Windows\System32\dwm.exe", className: "Ghost");
        _system.AddWindow(2, 20, Left);
        _system.NotResponding.Add(2);

        Assert.Equal(new ChosenApp(1, 2, 20, EndRefusal.None), _chooser.Describe(1));
    }

    [Fact]
    public void AStandInForNoneOrSeveralWindowsCantTellWhich()
    {
        _system.AddWindow(1, 90, Left, @"C:\Windows\System32\dwm.exe", className: "Ghost");
        Assert.Equal(EndRefusal.CantTellWhich, _chooser.Describe(1).Refusal);

        _system.HiddenWindows.Add((2, 20, Left));
        _system.HiddenWindows.Add((3, 30, Left));
        _system.AddProcess(20, @"C:\Apps\A.exe");
        _system.AddProcess(30, @"C:\Apps\B.exe");
        _system.NotResponding.UnionWith([2, 3]);

        var chosen = _chooser.Describe(1);

        Assert.Equal(EndRefusal.CantTellWhich, chosen.Refusal);
        Assert.Equal(0u, chosen.ProcessId);
    }

    [Fact]
    public void AStoreAppInTheSharedFrameIsItsCoreWindowsProcess()
    {
        _system.AddWindow(1, 70, Left, @"C:\Windows\System32\ApplicationFrameHost.exe", className: "ApplicationFrameWindow");
        _system.HiddenWindows.Add((2, 80, Left));
        _system.AddProcess(80, @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe");
        _system.CoreWindows[1] = 2;

        Assert.Equal(new ChosenApp(1, 2, 80, EndRefusal.None), _chooser.Describe(1));
    }

    [Fact]
    public void ASharedFrameWithoutItsAppCantTellWhich()
    {
        _system.AddWindow(1, 70, Left, @"C:\Windows\System32\ApplicationFrameHost.exe", className: "ApplicationFrameWindow");

        Assert.Equal(EndRefusal.CantTellWhich, _chooser.Describe(1).Refusal);
    }

    [Fact]
    public void NoWindowIsNothing()
    {
        Assert.Equal(ChosenApp.Nothing, _chooser.Describe(0));
        Assert.False(ChosenApp.Nothing.CanEnd);
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe", EndRefusal.FileExplorer)]
    [InlineData(@"c:\windows\EXPLORER.EXE", EndRefusal.FileExplorer)]
    [InlineData(@"C:\Windows\System32\dwm.exe", EndRefusal.PartOfWindows)]
    [InlineData(@"C:\Windows\SystemApps\ShellExperienceHost_cw5n1h2txyewy\ShellExperienceHost.exe", EndRefusal.PartOfWindows)]
    [InlineData(@"C:\Windows\System32\svchost.exe", EndRefusal.PartOfWindows)]
    [InlineData(@"C:\Apps\svchost.exe", EndRefusal.None)]
    [InlineData(@"C:\Apps\explorer.exe", EndRefusal.None)]
    [InlineData(@"C:\WindowsApps\dwm.exe", EndRefusal.None)]
    [InlineData(@"C:\Windows\System32\notepad.exe", EndRefusal.None)]
    [InlineData(null, EndRefusal.None)]
    public void FileExplorerAndWindowsOwnProcessesAreKnownByTheirProgramInTheWindowsFolder(string? program, EndRefusal refusal)
    {
        Assert.Equal(refusal, AppChooser.RefusalForProgram(program, @"C:\Windows"));
    }

    [Fact]
    public void TheDesktopAndTheTaskbarAreFileExplorers()
    {
        _system.AddWindow(1, 50, Left, @"C:\Windows\explorer.exe", className: "Shell_TrayWnd");

        Assert.Equal(EndRefusal.FileExplorer, _chooser.Describe(1).Refusal);
    }

    [Fact]
    public void PcCompanionsHostNeverEnds()
    {
        Assert.Equal(EndRefusal.PartOfPcCompanion, _chooser.RefusalFor(FakeEndAppSystem.Host));
    }

    [Fact]
    public void ACriticalProcessIsPartOfWindows()
    {
        _system.AddProcess(10, @"C:\Tools\Guard.exe").IsCritical = true;

        Assert.Equal(EndRefusal.PartOfWindows, _chooser.RefusalFor(10));
    }

    [Fact]
    public void AProcessWindowsRefusesRunsAsAdministratorOrIsNotAllowed()
    {
        _system.AddProcess(10, @"C:\Apps\Admin.exe");
        _system.AddProcess(20, @"C:\Apps\Other.exe");
        _system.Refused.UnionWith([10, 20]);
        _system.Elevated.Add(10);

        Assert.Equal(EndRefusal.RunsAsAdministrator, _chooser.RefusalFor(10));
        Assert.Equal(EndRefusal.NotAllowed, _chooser.RefusalFor(20));
    }

    [Fact]
    public void EachProcessIsOpenedOnceWhileChoosingAndClosedAgain()
    {
        var process = _system.AddProcess(10, @"C:\Apps\App.exe");

        _chooser.RefusalFor(10);
        _chooser.RefusalFor(10);
        Assert.Equal([10u], _system.Opened);
        Assert.Equal(1, process.Disposals);

        _chooser.Forget();
        _chooser.RefusalFor(10);
        Assert.Equal([10u, 10u], _system.Opened);
    }

    [Fact]
    public void HelpersAreDescendantsThatRunTheSameProgram()
    {
        const string Browser = @"C:\Apps\Browser.exe";
        var later = FakeEndAppSystem.Started.AddSeconds(1);
        _system.AddProcess(10, Browser);
        _system.AddProcess(11, Browser, parent: 10, start: later);
        _system.AddProcess(12, @"C:\Apps\Helper.exe", parent: 10, start: later);
        _system.AddProcess(13, Browser, parent: 11, start: later.AddSeconds(1));
        _system.AddProcess(14, Browser, parent: 12, start: later.AddSeconds(1));
        _system.AddProcess(15, @"c:\apps\browser.EXE", parent: 10, start: later);
        _system.AddProcess(16, Browser, parent: 99, start: later);

        var helpers = AppChooser.Helpers(_system, 10, FakeEndAppSystem.Started, Browser);

        Assert.Equal([11u, 15u, 13u], helpers);
    }

    [Fact]
    public void AProcessStartedBeforeItsParentIsntItsHelper()
    {
        const string Browser = @"C:\Apps\Browser.exe";
        _system.AddProcess(10, Browser);
        _system.AddProcess(11, Browser, parent: 10, start: FakeEndAppSystem.Started.AddSeconds(-1));

        Assert.Empty(AppChooser.Helpers(_system, 10, FakeEndAppSystem.Started, Browser));
    }

    [Fact]
    public void WithoutTheAppsStartTimeOrProgramThereAreNoHelpers()
    {
        _system.AddProcess(10, @"C:\Apps\Browser.exe");
        _system.AddProcess(11, @"C:\Apps\Browser.exe", parent: 10, start: FakeEndAppSystem.Started.AddSeconds(1));

        Assert.Empty(AppChooser.Helpers(_system, 10, null, @"C:\Apps\Browser.exe"));
        Assert.Empty(AppChooser.Helpers(_system, 10, FakeEndAppSystem.Started, null));
    }
}
