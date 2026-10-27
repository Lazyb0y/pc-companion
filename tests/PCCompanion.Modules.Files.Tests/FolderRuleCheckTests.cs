using System;
using System.Linq;
using PCCompanion.Modules.Files.FolderRules;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class FolderRuleCheckTests
{
    private const string Downloads = @"C:\Users\Ana\Downloads";

    private static readonly SpecialFolders Places = new(
        @"C:\Windows",
        [@"C:\Program Files", @"C:\Program Files (x86)"],
        @"C:\Users\Ana\AppData\Local\PCCompanion",
        [@"C:\Users\Ana\AppData", @"C:\ProgramData"]);

    private static readonly FolderRule Rule = new()
    {
        Name = "Installers",
        Extensions = ["exe", "msi"],
        Action = "Move",
        Destination = @"C:\Users\Ana\Downloads\Installers",
    };

    [Theory]
    [InlineData(Downloads)]
    [InlineData(@"C:\Users\Ana\Downloads\")]
    [InlineData("C:/Users/Ana/Downloads")]
    [InlineData(@"D:\")]
    [InlineData(@"\\server\share\Scans")]
    [InlineData(@"C:\Windows.old")]
    [InlineData(@"C:\Users\Ana\AppData\Roaming\App")]
    public void TheseFoldersCanBeWatched(string path)
    {
        Assert.Equal(FolderProblem.None, FolderRuleCheck.CheckFolder(path, Places));
    }

    [Theory]
    [InlineData("", FolderProblem.NoPath)]
    [InlineData("  ", FolderProblem.NoPath)]
    [InlineData("Downloads", FolderProblem.NotFullPath)]
    [InlineData("C:Downloads", FolderProblem.NotFullPath)]
    [InlineData(@"\Users\Ana", FolderProblem.NotFullPath)]
    [InlineData(@"\\", FolderProblem.NotFullPath)]
    [InlineData(@"\\server", FolderProblem.NotFullPath)]
    [InlineData(@"\\server\", FolderProblem.NotFullPath)]
    [InlineData(@"C:\Users\Ana\Down|loads", FolderProblem.InvalidPath)]
    [InlineData(@"C:\Users\*", FolderProblem.InvalidPath)]
    [InlineData(@"C:\Users\Ana\notes.txt:hidden", FolderProblem.InvalidPath)]
    [InlineData(@"\\?\C:\Users\Ana\Downloads", FolderProblem.InvalidPath)]
    [InlineData(@"\\.\C:\Users\Ana\Downloads", FolderProblem.InvalidPath)]
    [InlineData(@"C:\Windows", FolderProblem.WindowsFolder)]
    [InlineData(@"c:\windows\system32", FolderProblem.WindowsFolder)]
    [InlineData(@"C:\Users\..\Windows\Temp", FolderProblem.WindowsFolder)]
    [InlineData(@"C:\Program Files (x86)\App", FolderProblem.ProgramFiles)]
    [InlineData(@"C:\Users\Ana\AppData\Local\PCCompanion", FolderProblem.DataFolder)]
    [InlineData(@"C:\Users\Ana\AppData\Local\PCCompanion\Logs", FolderProblem.DataFolder)]
    public void TheseFoldersCantBeWatched(string path, FolderProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.CheckFolder(path, Places));
    }

    [Theory]
    [InlineData(@"C:\Users\Ana\AppData\Roaming\App", true)]
    [InlineData(@"C:\ProgramData\App", true)]
    [InlineData(@"C:\ProgramData", true)]
    [InlineData(Downloads, false)]
    [InlineData(@"C:\ProgramDataBackup", false)]
    public void FoldersWhereProgramsKeepTheirFilesGetAWarning(string path, bool warning)
    {
        Assert.Equal(warning, FolderRuleCheck.IsProgramData(path, Places));
    }

    [Theory]
    [InlineData(@"D:\Invoices", FolderProblem.None)]
    [InlineData(@"D:\Invoices\{year}\{month}", FolderProblem.None)]
    [InlineData(@"D:\Invoices {year}", FolderProblem.None)]
    [InlineData(@"D:\{extension}", FolderProblem.None)]
    [InlineData("", FolderProblem.NoPath)]
    [InlineData("{extension}", FolderProblem.NotFullPath)]
    [InlineData(@"Sorted\{extension}", FolderProblem.NotFullPath)]
    [InlineData(@"C:\Windows\{extension}", FolderProblem.WindowsFolder)]
    [InlineData(@"C:\Users\Ana\AppData\Local\PCCompanion\{year}", FolderProblem.DataFolder)]
    public void ADestinationIsCheckedByTheFolderItStartsWith(string destination, FolderProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.CheckDestination(destination, Places));
    }

    [Fact]
    public void ARuleWithANameAConditionAndAnActionCanBeUsed()
    {
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(Rule, Downloads, Places));
    }

    [Theory]
    [InlineData("", RuleProblem.NoName)]
    [InlineData("   ", RuleProblem.NoName)]
    [InlineData("Two\nlines", RuleProblem.InvalidName)]
    public void ARuleNeedsAName(string name, RuleProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.Check(Rule with { Name = name }, Downloads, Places));
    }

    [Fact]
    public void ANameMayHaveSixtyCharacters()
    {
        Assert.Equal(RuleProblem.None, FolderRuleCheck.CheckName(new string('a', 60)));
        Assert.Equal(RuleProblem.NameTooLong, FolderRuleCheck.CheckName(new string('a', 61)));
    }

    [Fact]
    public void ARuleNeedsACondition()
    {
        Assert.Equal(RuleProblem.NoCondition, FolderRuleCheck.Check(Rule with { Extensions = [] }, Downloads, Places));
    }

    [Fact]
    public void AnyConditionCounts()
    {
        var none = Rule with { Extensions = [] };

        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(none with { Names = ["scan*"] }, Downloads, Places));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(none with { Size = new SizeCondition { LargerThan = new SizeLimit { Amount = 1 } } }, Downloads, Places));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(none with { Date = new DateCondition { MoreThanDaysAgo = 30 } }, Downloads, Places));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(none with { DownloadedFrom = ["*.example.com"] }, Downloads, Places));
    }

    [Theory]
    [InlineData("invoice*", RuleProblem.None)]
    [InlineData("Report ????.pdf", RuleProblem.None)]
    [InlineData("", RuleProblem.InvalidNamePattern)]
    [InlineData(" ", RuleProblem.InvalidNamePattern)]
    [InlineData(@"scans\*", RuleProblem.InvalidNamePattern)]
    [InlineData("a|b", RuleProblem.InvalidNamePattern)]
    public void NamePatternsAreFileNamesWithWildcards(string pattern, RuleProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.CheckNames(["*.pdf", pattern]));
        Assert.Equal(problem, FolderRuleCheck.Check(Rule with { Names = [pattern] }, Downloads, Places));
    }

    [Theory]
    [InlineData("pdf", RuleProblem.None)]
    [InlineData(".pdf", RuleProblem.None)]
    [InlineData("", RuleProblem.InvalidExtension)]
    [InlineData(".", RuleProblem.InvalidExtension)]
    [InlineData("tar.gz", RuleProblem.InvalidExtension)]
    [InlineData("p*f", RuleProblem.InvalidExtension)]
    public void ExtensionsHaveNoDotsOrWildcards(string extension, RuleProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.CheckExtensions(["docx", extension]));
    }

    [Fact]
    public void ASizeIsAWholeNumberInAKnownUnit()
    {
        static SizeCondition Larger(long amount, SizeUnit unit = SizeUnit.MB) => new() { LargerThan = new SizeLimit { Amount = amount, Unit = unit } };

        Assert.Equal(RuleProblem.None, FolderRuleCheck.CheckSize(Larger(1)));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.CheckSize(Larger(FolderRuleCheck.MaximumSizeAmount, SizeUnit.GB)));
        Assert.Equal(RuleProblem.InvalidSize, FolderRuleCheck.CheckSize(Larger(0)));
        Assert.Equal(RuleProblem.InvalidSize, FolderRuleCheck.CheckSize(Larger(FolderRuleCheck.MaximumSizeAmount + 1)));
        Assert.Equal(RuleProblem.InvalidSize, FolderRuleCheck.CheckSize(Larger(1, (SizeUnit)7)));
        Assert.Equal(RuleProblem.InvalidSize, FolderRuleCheck.CheckSize(new SizeCondition()));
    }

    [Fact]
    public void ASizeRangeMustHoldAFile()
    {
        var range = new SizeCondition { LargerThan = new SizeLimit { Amount = 1024, Unit = SizeUnit.KB }, SmallerThan = new SizeLimit { Amount = 1, Unit = SizeUnit.MB } };

        Assert.Equal(RuleProblem.EmptySizeRange, FolderRuleCheck.CheckSize(range));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.CheckSize(range with { SmallerThan = new SizeLimit { Amount = 2, Unit = SizeUnit.MB } }));
    }

    [Theory]
    [InlineData(30, null, RuleProblem.None)]
    [InlineData(0, null, RuleProblem.None)]
    [InlineData(null, 7, RuleProblem.None)]
    [InlineData(7, 30, RuleProblem.None)]
    [InlineData(null, null, RuleProblem.InvalidDate)]
    [InlineData(-1, null, RuleProblem.InvalidDate)]
    [InlineData(null, 0, RuleProblem.InvalidDate)]
    [InlineData(36_501, null, RuleProblem.InvalidDate)]
    [InlineData(30, 30, RuleProblem.EmptyDateRange)]
    [InlineData(30, 7, RuleProblem.EmptyDateRange)]
    public void DaysAreInRangeAndLeaveRoomForAFile(int? more, int? less, RuleProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.CheckDate(new DateCondition { MoreThanDaysAgo = more, LessThanDaysAgo = less }));
    }

    [Fact]
    public void ADateOfAnUnknownKindCantBeUsed()
    {
        Assert.Equal(RuleProblem.InvalidDate, FolderRuleCheck.CheckDate(new DateCondition { Which = (FileDate)5, MoreThanDaysAgo = 1 }));
    }

    [Theory]
    [InlineData("*.example.com", RuleProblem.None)]
    [InlineData("files-1.example.co.uk", RuleProblem.None)]
    [InlineData("*.münchen.example", RuleProblem.None)]
    [InlineData("", RuleProblem.InvalidSitePattern)]
    [InlineData("https://example.com", RuleProblem.InvalidSitePattern)]
    [InlineData("example.com/downloads", RuleProblem.InvalidSitePattern)]
    [InlineData("example .com", RuleProblem.InvalidSitePattern)]
    public void SitePatternsAreSiteNamesWithWildcards(string site, RuleProblem problem)
    {
        Assert.Equal(problem, FolderRuleCheck.CheckSites([site]));
    }

    [Fact]
    public void AConditionsProblemKeepsTheRuleFromBeingUsed()
    {
        Assert.Equal(RuleProblem.InvalidExtension, FolderRuleCheck.Check(Rule with { Extensions = ["tar.gz"] }, Downloads, Places));
        Assert.Equal(RuleProblem.InvalidSize, FolderRuleCheck.Check(Rule with { Size = new SizeCondition() }, Downloads, Places));
        Assert.Equal(RuleProblem.InvalidDate, FolderRuleCheck.Check(Rule with { Date = new DateCondition() }, Downloads, Places));
        Assert.Equal(RuleProblem.InvalidSitePattern, FolderRuleCheck.Check(Rule with { DownloadedFrom = ["https://example.com"] }, Downloads, Places));
    }

    [Fact]
    public void ARuleWithAnActionThisVersionDoesntKnowCantBeUsed()
    {
        Assert.Equal(RuleProblem.UnknownAction, FolderRuleCheck.Check(Rule with { Action = "Extract" }, Downloads, Places));
    }

    [Theory]
    [InlineData("Move")]
    [InlineData("Copy")]
    public void AMoveOrCopyNeedsADestinationThatsAllowed(string action)
    {
        Assert.Equal(RuleProblem.NoDestination, FolderRuleCheck.Check(Rule with { Action = action, Destination = " " }, Downloads, Places));
        Assert.Equal(RuleProblem.DestinationNotAllowed, FolderRuleCheck.Check(Rule with { Action = action, Destination = @"C:\Program Files\App" }, Downloads, Places));
        Assert.Equal(RuleProblem.DestinationNotAllowed, FolderRuleCheck.Check(Rule with { Action = action, Destination = "Installers" }, Downloads, Places));
    }

    [Theory]
    [InlineData(Downloads)]
    [InlineData(@"c:\users\ana\downloads\")]
    [InlineData(@"C:\Users\Ana\Downloads\Installers\..")]
    public void AMoveOrCopyCantGoToItsOwnFolder(string destination)
    {
        Assert.Equal(RuleProblem.DestinationIsOwnFolder, FolderRuleCheck.Check(Rule with { Destination = destination }, Downloads, Places));
        Assert.Equal(RuleProblem.DestinationIsOwnFolder, FolderRuleCheck.Check(Rule with { Action = "Copy", Destination = destination }, Downloads, Places));
    }

    [Fact]
    public void AMoveMayGoToASubfolderMadeFromTheFile()
    {
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(Rule with { Destination = Downloads + @"\{extension}" }, Downloads, Places));
    }

    [Fact]
    public void ARenameNeedsANewName()
    {
        var rename = Rule with { Action = "Rename", Destination = string.Empty };

        Assert.Equal(RuleProblem.NoNewName, FolderRuleCheck.Check(rename, Downloads, Places));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(rename with { NewName = "{date} {name}" }, Downloads, Places));
    }

    [Fact]
    public void WhatToDoWithATakenNameMustBeKnownWhereItsUsed()
    {
        var unknown = Rule with { NewName = "{name}", IfExists = "Replace" };

        Assert.Equal(RuleProblem.UnknownIfExists, FolderRuleCheck.Check(unknown, Downloads, Places));
        Assert.Equal(RuleProblem.UnknownIfExists, FolderRuleCheck.Check(unknown with { Action = "Rename" }, Downloads, Places));
        Assert.Equal(RuleProblem.None, FolderRuleCheck.Check(unknown with { Action = "RecycleBin" }, Downloads, Places));
    }

    [Fact]
    public void FoldersAfterTheFirstTenAreLeftOutWithTheirRules()
    {
        var settings = new FolderRulesSettings
        {
            Folders = [.. Enumerable.Range(1, 11).Select(number => new WatchedFolder { Path = $@"D:\Folder {number}", Rules = [Rule with { Destination = @"D:\Sorted" }] })],
        };

        var reviews = FolderRuleCheck.Review(settings, Places);

        Assert.All(reviews.Take(10), review => Assert.Equal(FolderProblem.None, review.Problem));
        Assert.Equal(FolderProblem.TooMany, reviews[10].Problem);
        Assert.Equal([RuleProblem.TooMany], reviews[10].Rules);
    }

    [Fact]
    public void RulesAfterTheFirstFiftyInAllFoldersAreLeftOut()
    {
        var rules = Enumerable.Range(1, 30).Select(_ => Rule with { Destination = @"D:\Sorted" }).ToArray();
        var settings = new FolderRulesSettings
        {
            Folders = [new WatchedFolder { Path = @"D:\One", Rules = rules }, new WatchedFolder { Path = @"D:\Two", Rules = rules }],
        };

        var reviews = FolderRuleCheck.Review(settings, Places);

        Assert.All(reviews[0].Rules, problem => Assert.Equal(RuleProblem.None, problem));
        Assert.Equal(20, reviews[1].Rules.Count(problem => problem == RuleProblem.None));
        Assert.Equal(10, reviews[1].Rules.Skip(20).Count(problem => problem == RuleProblem.TooMany));
    }

    [Fact]
    public void AFolderListedTwiceIsWatchedOnce()
    {
        var settings = new FolderRulesSettings
        {
            Folders =
            [
                new WatchedFolder { Path = Downloads },
                new WatchedFolder { Path = @"c:\users\ana\downloads\", Enabled = false },
            ],
        };

        var reviews = FolderRuleCheck.Review(settings, Places);

        Assert.Equal(FolderProblem.None, reviews[0].Problem);
        Assert.Equal(FolderProblem.AlreadyWatched, reviews[1].Problem);
    }

    [Fact]
    public void TheRulesOfAFolderThatCantBeWatchedAreStillChecked()
    {
        var settings = new FolderRulesSettings
        {
            Folders = [new WatchedFolder { Path = @"C:\Windows\Temp", Rules = [Rule, Rule with { Name = string.Empty }] }],
        };

        var review = Assert.Single(FolderRuleCheck.Review(settings, Places));

        Assert.Equal(FolderProblem.WindowsFolder, review.Problem);
        Assert.Equal([RuleProblem.None, RuleProblem.NoName], review.Rules);
    }

    [Fact]
    public void TheCurrentUsersFoldersAreRefusedAndWarnedAbout()
    {
        var dataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\PCCompanion";
        var places = SpecialFolders.ForCurrentUser(dataFolder);

        Assert.Equal(FolderProblem.WindowsFolder, FolderRuleCheck.CheckFolder(Environment.GetFolderPath(Environment.SpecialFolder.System), places));
        Assert.Equal(FolderProblem.ProgramFiles, FolderRuleCheck.CheckFolder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), places));
        Assert.Equal(FolderProblem.DataFolder, FolderRuleCheck.CheckFolder(dataFolder, places));
        Assert.True(FolderRuleCheck.IsProgramData(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), places));
        Assert.False(FolderRuleCheck.IsProgramData(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), places));
    }
}
