using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Windows.WindowTabs;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class KeptGroupsTests
{
    private static readonly KeptTab Mail = new() { AppId = "Mail" };
    private static readonly KeptTab Notes = new() { Program = @"C:\Apps\Notes.exe" };
    private static readonly KeptTab Report = new() { AppId = "Report" };

    private readonly KeptGroups _kept = new();

    [Fact]
    public void AKeptGroupIsSavedWithItsTabsAndTheOneInFront()
    {
        var key = new object();

        Assert.True(_kept.Keep(key, [Mail, Notes], 1));

        var saved = Assert.Single(_kept.Saved);
        Assert.Equal([Mail, Notes], saved.Tabs);
        Assert.Equal(1, saved.Front);
    }

    [Fact]
    public void KeepingTheSameAgainChangesNothingAndAChangeComesFirst()
    {
        object first = new(), second = new();
        _kept.Keep(first, [Mail, Notes], 0);
        _kept.Keep(second, [Report, Notes], 0);

        Assert.False(_kept.Keep(first, [Mail, Notes], 0));
        Assert.Equal([Report, Notes], _kept.Saved[0].Tabs);

        Assert.True(_kept.Keep(first, [Mail, Notes], 1));
        Assert.Equal([Mail, Notes], _kept.Saved[0].Tabs);
        Assert.Equal(1, _kept.Saved[0].Front);
        Assert.Equal(2, _kept.Count);
    }

    [Fact]
    public void ATabWhoseAppCantBeKeptIsLeftOutAndAGroupOfOneIsntSaved()
    {
        _kept.Keep(new object(), [Mail, null, Notes], 2);
        _kept.Keep(new object(), [null, Report], 1);

        var saved = Assert.Single(_kept.Saved);
        Assert.Equal([Mail, Notes], saved.Tabs);
        Assert.Equal(1, saved.Front);
    }

    [Fact]
    public void AtMostFiftyGroupsAreKeptAndTheOneChangedLongestAgoGoes()
    {
        var keys = Enumerable.Range(0, KeptGroups.Limit + 1).Select(_ => new object()).ToList();
        for (var index = 0; index < keys.Count; index++)
        {
            _kept.Keep(keys[index], [Mail, new KeptTab { Program = $@"C:\Apps\App{index}.exe" }], 0);
        }

        Assert.Equal(KeptGroups.Limit, _kept.Count);
        Assert.Equal(@"C:\Apps\App50.exe", _kept.Saved[0].Tabs[1]!.Program);
        Assert.Equal(@"C:\Apps\App1.exe", _kept.Saved[^1].Tabs[1]!.Program);
    }

    [Fact]
    public void ForgettingAGroupOrAllOfThemRemovesThem()
    {
        object first = new(), second = new();
        _kept.Keep(first, [Mail, Notes], 0);
        _kept.Keep(second, [Report, Notes], 0);

        Assert.True(_kept.Forget(first));
        Assert.False(_kept.Forget(first));
        Assert.Equal([Report, Notes], Assert.Single(_kept.Saved).Tabs);

        Assert.True(_kept.ForgetAll());
        Assert.False(_kept.ForgetAll());
        Assert.Empty(_kept.Saved);
    }

    [Fact]
    public void GroupsReadFromTheFileComeAfterTheOnesKeptSinceAndWait()
    {
        _kept.Keep(new object(), [Report, Notes], 0);

        var added = _kept.Load([new KeptGroup { Tabs = [Mail, Notes], Front = 1 }]);

        Assert.Equal(1, added);
        Assert.Equal(1, _kept.Waiting);
        Assert.Equal([Report, Notes], _kept.Saved[0].Tabs);
        Assert.Equal([Mail, Notes], _kept.Saved[1].Tabs);
        Assert.Equal(1, _kept.Saved[1].Front);
    }

    [Fact]
    public void LoadLeavesOutTabsWithNeitherOrBothAnAppIdAndAProgramAndGroupsOfOne()
    {
        var added = _kept.Load(
        [
            null,
            new KeptGroup { Tabs = [new KeptTab { AppId = "A", Program = "B" }, Mail, new KeptTab { AppId = " " }, Notes], Front = 0 },
            new KeptGroup { Tabs = [null, Report], Front = 1 },
            new KeptGroup { Tabs = [Mail, Report], Front = 5 },
        ]);

        Assert.Equal(2, added);
        Assert.Equal([Mail, Notes], _kept.Saved[0].Tabs);
        Assert.Equal(0, _kept.Saved[0].Front);
        Assert.Equal(0, _kept.Saved[1].Front);
    }

    [Fact]
    public void LoadAddsUpToTheLimit()
    {
        var groups = Enumerable.Range(0, KeptGroups.Limit + 5).Select(_ => new KeptGroup { Tabs = [Mail, Notes] });

        Assert.Equal(KeptGroups.Limit, _kept.Load(groups));
    }

    [Fact]
    public void AGroupIsFoundOnceEachTabHasAWindowOfItsApp()
    {
        _kept.Load([new KeptGroup { Tabs = [Mail, Notes], Front = 1 }]);

        Assert.Empty(_kept.Match([(1, App(appId: "Mail"))], final: false));

        var found = Assert.Single(_kept.Match([(1, App(appId: "mail")), (2, App(program: @"c:\apps\notes.exe"))], final: false));
        Assert.Equal([1, 2], found.Windows);
        Assert.Equal(1, found.Front);
        Assert.Equal(0, _kept.Waiting);
        Assert.Empty(_kept.Match([(1, App(appId: "Mail")), (2, App(program: @"C:\Apps\Notes.exe"))], final: false));

        // The group made again is known by the key, so keeping it changes the entry read from the file.
        Assert.True(_kept.Keep(found.Key, [Mail, Notes], 0));
        Assert.Equal(1, _kept.Count);
    }

    [Fact]
    public void EachTabTakesTheFrontMostWindowOfItsAppThatNoOtherTabTook()
    {
        _kept.Load(
        [
            new KeptGroup { Tabs = [Mail, Mail], Front = 0 },
            new KeptGroup { Tabs = [Mail, Notes], Front = 0 },
        ]);

        var found = _kept.Match([(5, App(appId: "Mail")), (6, App(appId: "Mail")), (7, App(program: @"C:\Apps\Notes.exe")), (8, App(appId: "Mail"))], final: false);

        Assert.Equal([5, 6], found[0].Windows);
        Assert.Equal([8, 7], found[1].Windows);
    }

    [Fact]
    public void AProgramMatchesOnlyAWindowWithoutAnAppId()
    {
        _kept.Load([new KeptGroup { Tabs = [Mail, Notes] }]);

        Assert.Empty(_kept.Match([(1, App(appId: "Mail")), (2, App(appId: "Other", program: @"C:\Apps\Notes.exe"))], final: false));
    }

    [Fact]
    public void OnceTheTimeIsUpAGroupIsMadeOfTheWindowsThereWhenAtLeastTwoAre()
    {
        _kept.Load(
        [
            new KeptGroup { Tabs = [Mail, Notes, Report], Front = 1 },
            new KeptGroup { Tabs = [Notes, Report], Front = 0 },
        ]);
        var candidates = new List<(nint, AppOfWindow)> { (3, App(appId: "Report")), (1, App(appId: "Mail")) };

        Assert.Empty(_kept.Match(candidates, final: false));
        var found = Assert.Single(_kept.Match(candidates, final: true));

        // Without the remembered tab in front, the front-most window is.
        Assert.Equal([1, 3], found.Windows);
        Assert.Equal(1, found.Front);
        Assert.Equal(1, _kept.Waiting);
    }

    [Fact]
    public void AGroupNotFoundIsKeptWhenTheWaitingStops()
    {
        _kept.Load([new KeptGroup { Tabs = [Mail, Notes] }]);

        _kept.StopWaiting();

        Assert.Equal(0, _kept.Waiting);
        Assert.Empty(_kept.Match([(1, App(appId: "Mail")), (2, App(program: @"C:\Apps\Notes.exe"))], final: true));
        Assert.Single(_kept.Saved);
    }

    [Fact]
    public void AWindowAppIsKeptByItsAppIdOtherwiseItsProgram()
    {
        Assert.Equal(new KeptTab { AppId = "Mail" }, KeptTab.Of(App(appId: "Mail", program: @"C:\Apps\Mail.exe")));
        Assert.Equal(new KeptTab { Program = @"C:\Apps\Notes.exe" }, KeptTab.Of(App(program: @"C:\Apps\Notes.exe")));
        Assert.Null(KeptTab.Of(App()));
    }

    private static AppOfWindow App(string? appId = null, string? program = null) => new(appId, null, program, 1);
}
