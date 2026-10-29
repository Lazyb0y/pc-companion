using System;
using System.Globalization;
using System.Linq;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TrayControllerTests : IDisposable
{
    private readonly CultureInfo _language = CultureInfo.CurrentUICulture;
    private readonly FakeTrayWindowsSystem _system = new();
    private readonly FakeTrayIcons _icons = new();
    private readonly ManualTimeProvider _clock = new();
    private readonly TrayController _tray;

    public TrayControllerTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        _tray = new TrayController(_system, _icons, _clock, action => action(), new RecordingLogger<TrayController>());
        _tray.Apply(new MinimizeToTraySettings { Enabled = true });
    }

    public void Dispose()
    {
        _tray.Dispose();
        CultureInfo.CurrentUICulture = _language;
    }

    [Fact]
    public void TheShortcutSendsTheWindowInFrontToTheTray()
    {
        var window = _system.Add(1, title: "Notes - Editor");
        _system.ForegroundWindow = 1;

        Assert.Equal(SendOutcome.Sent, _tray.SendFront());

        Assert.False(window.Visible);
        Assert.True(window.Minimized);
        Assert.True(window.Marked);
        Assert.Equal(["mark 1", "minimize 1", "watch 10", "hide 1"], _system.Calls);
        var icon = Assert.Single(_icons.Icons.Values);
        Assert.Equal(("App", "Notes - Editor"), (icon.Name, icon.ToolTip));
        Assert.Equal(FakeTrayWindowsSystem.GlyphPixels, icon.Pixels);
        Assert.Equal([new TrayWindowInfo(1, "App", "Notes - Editor")], _tray.Windows);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void TheShortcutSendsTheMainWindowOfAPaletteInFront()
    {
        _system.Add(1);
        _system.Windows[2] = new FakeWindow { Handle = 2, ProcessId = 10, Owner = 1, OnTaskbar = false };
        _system.ForegroundWindow = 2;

        Assert.Equal(SendOutcome.Sent, _tray.SendFront());

        Assert.False(_system.Windows[1].Visible);
        Assert.Contains("hide 1", _system.Calls);
    }

    [Fact]
    public void WithNoWindowInFrontTheShortcutPlaysTheWarning()
    {
        Assert.Equal(SendOutcome.NoWindow, _tray.SendFront());

        Assert.Equal(1, _system.Warnings);
    }

    [Theory]
    [InlineData("taskbar", SendOutcome.NotOnTaskbar)]
    [InlineData("frame", SendOutcome.SharedAppFrame)]
    [InlineData("dialog", SendOutcome.DialogOpen)]
    [InlineData("administrator", SendOutcome.Refused)]
    public void AWindowThatCantGoIsLeftAsItIsWithTheWarning(string kind, SendOutcome expected)
    {
        _system.Windows[1] = new FakeWindow
        {
            Handle = 1,
            ProcessId = 10,
            Program = @"C:\Apps\App.exe",
            OnTaskbar = kind != "taskbar",
            ClassName = kind == "frame" ? "ApplicationFrameWindow" : "AppWindow",
            Enabled = kind != "dialog",
            Refuses = kind == "administrator",
        };
        _system.ForegroundWindow = 1;

        Assert.Equal(expected, _tray.SendFront());

        Assert.True(_system.Windows[1].Visible);
        Assert.False(_system.Windows[1].Minimized);
        Assert.False(_system.Windows[1].Marked);
        Assert.Empty(_icons.Icons);
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AWindowThatCantBeHiddenLeavesNoIconOrMark()
    {
        _system.Windows[1] = new FakeWindow { Handle = 1, ProcessId = 10, Program = @"C:\Apps\App.exe", RefusesHide = true };
        _system.ForegroundWindow = 1;

        // The first window of an app is on its way while the app's name is read, so the refusal comes later.
        _tray.SendFront();

        Assert.False(_system.Windows[1].Marked);
        Assert.Empty(_icons.Icons);
        Assert.Empty(_system.Watched);
        Assert.Empty(_tray.Windows);
        Assert.Equal(1, _system.Warnings);
    }

    [Fact]
    public void AWindowWaitsMinimizedUntilItsAppsNameIsRead()
    {
        _system.ReadsLooksAtOnce = false;
        var window = _system.Add(1);
        _system.ForegroundWindow = 1;

        Assert.Equal(SendOutcome.Sent, _tray.SendFront());
        Assert.True(window.Visible);
        Assert.True(window.Minimized);
        Assert.Empty(_icons.Icons);

        _system.FinishReadingLooks();

        Assert.False(window.Visible);
        Assert.Single(_icons.Icons);
    }

    [Fact]
    public void AWindowRestoredWhileItsAppsNameIsReadStaysOut()
    {
        _system.ReadsLooksAtOnce = false;
        var window = _system.Add(1);
        _system.ForegroundWindow = 1;
        _tray.SendFront();

        window.Minimized = false;
        _system.FinishReadingLooks();

        Assert.True(window.Visible);
        Assert.False(window.Marked);
        Assert.Empty(_icons.Icons);
    }

    [Fact]
    public void ShiftAndMinimizeSendTheMinimizingWindowWithoutMinimizingItAgain()
    {
        var window = _system.Add(1);
        window.Minimized = true;
        _system.IsShiftHeld = true;

        _tray.OnMinimizeStart(1);

        Assert.False(window.Visible);
        Assert.DoesNotContain("minimize 1", _system.Calls);
        Assert.Single(_icons.Icons);
    }

    [Fact]
    public void MinimizeWithoutShiftOrWithTheWindowsKeyLeavesTheWindowAlone()
    {
        _system.Add(1);
        _tray.OnMinimizeStart(1);
        _system.IsShiftHeld = true;
        _system.IsWindowsKeyHeld = true;
        _tray.OnMinimizeStart(1);

        Assert.True(_system.Windows[1].Visible);
        Assert.Empty(_icons.Icons);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void ShiftAndMinimizeTurnedOffLeavesTheWindowAlone()
    {
        _tray.Apply(new MinimizeToTraySettings { Enabled = true, ShiftMinimize = false });
        _system.Add(1);
        _system.IsShiftHeld = true;

        _tray.OnMinimizeStart(1);

        Assert.True(_system.Windows[1].Visible);
        Assert.False(_tray.WatchesMinimizes);
    }

    [Fact]
    public void ShiftAndMinimizeOnAWindowThatCantGoPlaysTheWarning()
    {
        _system.Windows[1] = new FakeWindow { Handle = 1, ProcessId = 10, Program = @"C:\Apps\App.exe", Refuses = true, Minimized = true };
        _system.IsShiftHeld = true;

        _tray.OnMinimizeStart(1);

        Assert.Equal(1, _system.Warnings);
        Assert.Empty(_icons.Icons);
    }

    [Fact]
    public void AListedAppsWindowGoesWhenItsMinimizedAlone()
    {
        ListApp(@"C:\Apps\App.exe");
        var window = _system.Add(1);
        window.Minimized = true;

        _tray.OnMinimizeStart(1);
        Assert.True(window.Visible);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.False(window.Visible);
        Assert.Single(_icons.Icons);
        Assert.Equal(0, _system.Warnings);
    }

    [Fact]
    public void AListedAppIsMatchedByItsProgramWhateverItsCase()
    {
        ListApp(@"c:\apps\APP.EXE");
        _system.Add(1).Minimized = true;

        _tray.OnMinimizeStart(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.False(_system.Windows[1].Visible);
    }

    [Fact]
    public void AnotherAppsWindowStaysOnTheTaskbar()
    {
        ListApp(@"C:\Apps\Other.exe");
        _system.Add(1).Minimized = true;

        _tray.OnMinimizeStart(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(_system.Windows[1].Visible);
    }

    [Fact]
    public void ShowDesktopSendsNoListedWindow()
    {
        ListApp(@"C:\Apps\App.exe");
        _system.Add(1).Minimized = true;
        _system.Add(2, processId: 20, program: @"C:\Apps\Other.exe").Minimized = true;

        _tray.OnMinimizeStart(2);
        _clock.Advance(TimeSpan.FromMilliseconds(30));
        _tray.OnMinimizeStart(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(_system.Windows[1].Visible);
        Assert.Empty(_icons.Icons);
    }

    [Fact]
    public void AnotherWindowMinimizingJustAfterAlsoKeepsAListedWindowOnTheTaskbar()
    {
        ListApp(@"C:\Apps\App.exe");
        _system.Add(1).Minimized = true;
        _system.Add(2, processId: 20, program: @"C:\Apps\Other.exe").Minimized = true;

        _tray.OnMinimizeStart(1);
        _clock.Advance(TimeSpan.FromMilliseconds(60));
        _tray.OnMinimizeStart(2);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(_system.Windows[1].Visible);
    }

    [Fact]
    public void AListedWindowMinimizedWithTheWindowsKeyHeldStaysOnTheTaskbar()
    {
        ListApp(@"C:\Apps\App.exe");
        _system.Add(1).Minimized = true;
        _system.IsWindowsKeyHeld = true;

        _tray.OnMinimizeStart(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(_system.Windows[1].Visible);
    }

    [Fact]
    public void AListedWindowThatCantGoStaysMinimizedWithoutTheWarning()
    {
        ListApp(@"C:\Apps\App.exe");
        _system.Windows[1] = new FakeWindow { Handle = 1, ProcessId = 10, Program = @"C:\Apps\App.exe", Refuses = true, Minimized = true };

        _tray.OnMinimizeStart(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, _system.Warnings);
        Assert.Empty(_icons.Icons);
    }

    [Fact]
    public void AnAppsWindowsShareOneIconWithTheirTitles()
    {
        SendFront(_system.Add(1, title: "First"));
        SendFront(_system.Add(2, title: "Second"));

        var icon = Assert.Single(_icons.Icons.Values);
        Assert.Equal("2 windows\nSecond\nFirst", icon.ToolTip);
        Assert.Equal("App", icon.Name);
        Assert.Equal([2, 1], _tray.Windows.Select(window => window.Window));
    }

    [Fact]
    public void EachAppGetsItsOwnIcon()
    {
        SendFront(_system.Add(1));
        SendFront(_system.Add(2, processId: 20, program: @"C:\Apps\Other.exe"));

        Assert.Equal(2, _icons.Icons.Count);
        Assert.Equal(["App", "Other"], _icons.Icons.Values.Select(icon => icon.Name).Order());
    }

    [Fact]
    public void AnAppsMenuListsItsWindowsThenBringAllBackAndSettings()
    {
        SendFront(_system.Add(1, title: "First"));
        SendFront(_system.Add(2, title: ""));

        var menu = _tray.MenuOf(_icons.Icons.Keys.Single());

        Assert.Equal(
            [
                new TrayMenuItem(TrayMenuCommand.BringBack, "App", 2),
                new TrayMenuItem(TrayMenuCommand.BringBack, "First", 1),
                new TrayMenuItem(TrayMenuCommand.BringAllBack, "Bring all back"),
                new TrayMenuItem(TrayMenuCommand.Settings, "Minimize to Tray settings"),
            ],
            menu);
    }

    [Fact]
    public void SelectingTheIconBringsBackTheWindowThatWentLastAndThenItsInFront()
    {
        SendFront(_system.Add(1));
        var last = _system.Add(2);
        SendFront(last);
        var id = _icons.Icons.Keys.Single();
        _system.Calls.Clear();

        Assert.True(_tray.BringBackApp(id));

        Assert.True(last.Visible);
        Assert.False(last.Marked);
        Assert.Equal(["unmark 2", "restore 2"], _system.Calls);
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(["unmark 2", "restore 2", "activate 2"], _system.Calls);
        Assert.Equal("Title", _icons.Icons[id].ToolTip);
    }

    [Fact]
    public void AWindowThatShowsLateComesToTheFrontOnceItShows()
    {
        _system.Windows[1] = new FakeWindow { Handle = 1, ProcessId = 10, Program = @"C:\Apps\App.exe", ShowsLate = true };
        SendFront(_system.Windows[1]);

        _tray.BringBack(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.DoesNotContain("activate 1", _system.Calls);

        _system.Windows[1].Visible = true;
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Single(_system.Calls, call => call == "activate 1");

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_system.Calls, call => call == "activate 1");
    }

    [Fact]
    public void BringAllBackBringsBackEveryWindowOfTheAppAndRemovesItsIcon()
    {
        SendFront(_system.Add(1));
        SendFront(_system.Add(2));
        var id = _icons.Icons.Keys.Single();

        _tray.Choose(id, new TrayMenuItem(TrayMenuCommand.BringAllBack, string.Empty));

        Assert.True(_system.Windows[1].Visible && _system.Windows[2].Visible);
        Assert.Empty(_icons.Icons);
        Assert.Empty(_system.Watched);
        Assert.Empty(_tray.Windows);
    }

    [Fact]
    public void AWindowItsAppShowsAgainLeavesTheTray()
    {
        var window = _system.Add(1);
        SendFront(window);

        window.Visible = true;
        _tray.OnShown(1);

        Assert.False(window.Marked);
        Assert.Empty(_icons.Icons);
        Assert.Empty(_system.Watched);
        Assert.DoesNotContain("restore 1", _system.Calls);
        Assert.Empty(_tray.Windows);
    }

    [Fact]
    public void AWindowThatClosesLeavesTheTrayAndItsProcessIsNoLongerWatched()
    {
        SendFront(_system.Add(1));
        SendFront(_system.Add(2));

        _system.Windows.Remove(1);
        _tray.OnDestroyed(1);
        Assert.Contains(10u, _system.Watched);
        Assert.Equal("Title", _icons.Icons.Values.Single().ToolTip);

        _system.Windows.Remove(2);
        _tray.OnDestroyed(2);

        Assert.Empty(_icons.Icons);
        Assert.Empty(_system.Watched);
    }

    [Fact]
    public void ATitleChangeShowsInTheTooltipAndTheIconIsReadAgain()
    {
        var window = _system.Add(1, title: "Inbox");
        SendFront(window);
        var requests = _system.IconRequests.Count;

        window.Title = "Inbox (3)";
        _tray.OnTitleChanged(1);

        Assert.Equal("Inbox (3)", _icons.Icons.Values.Single().ToolTip);
        Assert.Equal(requests + 1, _system.IconRequests.Count);
        Assert.Equal("Inbox (3)", _tray.Windows.Single().Title);
    }

    [Fact]
    public void TheIconShowsTheWindowsOwnIconOnceItAnswers()
    {
        _system.Windows[1] = new FakeWindow { Handle = 1, ProcessId = 10, Program = @"C:\Apps\App.exe", ClassIcon = FakeTrayWindowsSystem.Pixels(0xFF000002) };
        SendFront(_system.Windows[1]);
        Assert.Equal(FakeTrayWindowsSystem.Pixels(0xFF000002), _icons.Icons.Values.Single().Pixels);

        var own = FakeTrayWindowsSystem.Pixels(0xFF000003);
        _system.IconRequests.Single().Answered(own);

        Assert.Equal(own, _icons.Icons.Values.Single().Pixels);
    }

    [Fact]
    public void AnOlderWindowsIconDoesNotReplaceTheNewestOnes()
    {
        SendFront(_system.Add(1));
        SendFront(_system.Add(2));
        var older = _system.IconRequests.First(request => request.Window == 1);

        older.Answered(FakeTrayWindowsSystem.Pixels(0xFF000004));

        Assert.Equal(FakeTrayWindowsSystem.GlyphPixels, _icons.Icons.Values.Single().Pixels);
    }

    [Fact]
    public void AnUnchangedIconIsNotSentToWindowsAgain()
    {
        SendFront(_system.Add(1));
        var shows = _icons.Shows;

        _tray.OnTitleChanged(1);

        Assert.Equal(shows, _icons.Shows);
    }

    [Fact]
    public void ReleaseAllPutsEveryWindowBackOnTheTaskbarMinimized()
    {
        SendFront(_system.Add(1));
        SendFront(_system.Add(2, processId: 20, program: @"C:\Apps\Other.exe"));

        Assert.Equal(2, _tray.ReleaseAll());

        Assert.All(new[] { _system.Windows[1], _system.Windows[2] }, window =>
        {
            Assert.True(window.Visible);
            Assert.True(window.Minimized);
            Assert.False(window.Marked);
        });
        Assert.Empty(_icons.Icons);
        Assert.Empty(_system.Watched);
        Assert.DoesNotContain(_system.Calls, call => call.StartsWith("activate", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseAllAlsoUnmarksAWindowOnItsWayToTheTray()
    {
        _system.ReadsLooksAtOnce = false;
        var window = _system.Add(1);
        _system.ForegroundWindow = 1;
        _tray.SendFront();

        _tray.ReleaseAll();
        _system.FinishReadingLooks();

        Assert.False(window.Marked);
        Assert.True(window.Visible);
        Assert.Empty(_icons.Icons);
    }

    [Fact]
    public void ReleaseNowPutsTheWindowsBackFromTheListOfWindowsInTheTray()
    {
        SendFront(_system.Add(1));

        _tray.ReleaseNow();

        Assert.True(_system.Windows[1].Visible);
        Assert.False(_system.Windows[1].Marked);
    }

    [Fact]
    public void RecoverBringsBackMarkedHiddenWindowsAndUnmarksTheOthers()
    {
        var hidden = _system.Add(1);
        (hidden.Visible, hidden.Minimized, hidden.Marked) = (false, true, true);
        var shown = _system.Add(2);
        shown.Marked = true;
        var other = _system.Add(3);
        other.Visible = false;

        Assert.Equal(1, TrayController.Recover(_system));

        Assert.True(hidden.Visible);
        Assert.True(hidden.Minimized);
        Assert.False(hidden.Marked || shown.Marked);
        Assert.False(other.Visible);
    }

    [Fact]
    public void AWindowInTheTrayIsNotSentAgain()
    {
        var window = _system.Add(1);
        SendFront(window);
        _system.IsShiftHeld = true;

        _tray.OnMinimizeStart(1);

        Assert.Single(_system.Calls, call => call == "hide 1");
    }

    private void SendFront(FakeWindow window)
    {
        _system.ForegroundWindow = window.Handle;
        Assert.Equal(SendOutcome.Sent, _tray.SendFront());
    }

    private void ListApp(string program) =>
        _tray.Apply(new MinimizeToTraySettings { Enabled = true, MinimizedApps = [new MinimizedApp { Name = "App", Program = program }] });
}
