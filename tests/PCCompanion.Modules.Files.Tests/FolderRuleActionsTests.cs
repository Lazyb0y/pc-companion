using System;
using System.Threading;
using PCCompanion.Modules.Files.FolderRules;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class FolderRuleActionsTests
{
    private const string Downloads = @"D:\Downloads";
    private const string Invoice = @"D:\Downloads\Invoice.pdf";

    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", [@"C:\Users\Ana\AppData"])
    {
        Protected = [@"C:\Users\Ana\Documents", @"C:\Users\Ana\Pictures"],
    };

    private readonly FakeFolderRulesSystem _system = new FakeFolderRulesSystem().AddFile(Invoice).AddFolder(@"D:\Invoices");
    private readonly FolderRuleActions _actions;

    public FolderRuleActionsTests()
    {
        _actions = new FolderRuleActions(_system, Places, TimeZoneInfo.Utc);
    }

    private static FolderRule Rule(string action, string destination = @"D:\Invoices", string newName = "", string ifExists = "AddNumber") =>
        new() { Name = " Invoices ", Extensions = ["pdf"], Action = action, Destination = destination, NewName = newName, IfExists = ifExists };

    private HistoryEntry? Act(FolderRule rule, string path = Invoice) =>
        _actions.Act(Downloads, RuleMatcher.Create(rule, 1), _system.Read(path)!, FakeFolderRulesSystem.Time.AddHours(1), CancellationToken.None);

    [Fact]
    public void AMovePutsTheFileInItsDestination()
    {
        var file = _system.Read(Invoice)!;

        var entry = Act(Rule("Move"))!;

        Assert.False(_system.Exists(Invoice));
        Assert.True(_system.Exists(@"D:\Invoices\Invoice.pdf"));
        Assert.Equal(HistoryOutcome.Moved, entry.Outcome);
        Assert.Equal(@"D:\Invoices\Invoice.pdf", entry.NewPath);
        Assert.Equal("Invoices", entry.Rule);
        Assert.Equal("Invoice.pdf", entry.Name);
        Assert.Equal(Downloads, entry.Folder);
        Assert.Equal(FakeFolderRulesSystem.Time.AddHours(1), entry.Time);
        Assert.Equal(new FileIdentity(file.FileId, file.Size, file.Modified), entry.File);
    }

    [Fact]
    public void ACopyLeavesTheFile()
    {
        var entry = Act(Rule("Copy"))!;

        Assert.True(_system.Exists(Invoice));
        Assert.True(_system.Exists(@"D:\Invoices\Invoice.pdf"));
        Assert.Equal(HistoryOutcome.Copied, entry.Outcome);
    }

    [Fact]
    public void SubfoldersMadeFromTheFileAreCreated()
    {
        var entry = Act(Rule("Move", @"D:\Invoices\{year}\{month}"))!;

        Assert.Equal(@"D:\Invoices\2026\03\Invoice.pdf", entry.NewPath);
        Assert.True(_system.Exists(@"D:\Invoices\2026\03\Invoice.pdf"));
    }

    [Fact]
    public void AMoveCanGiveTheFileANewName()
    {
        var entry = Act(Rule("Move", newName: "{date} {name}"))!;

        Assert.Equal(@"D:\Invoices\2026-03-05 Invoice.pdf", entry.NewPath);
    }

    [Fact]
    public void ATakenNameGetsTheNextNumber()
    {
        _system.AddFile(@"D:\Invoices\Invoice.pdf").AddFile(@"D:\Invoices\Invoice (2).pdf");

        var entry = Act(Rule("Move"))!;

        Assert.Equal(@"D:\Invoices\Invoice (3).pdf", entry.NewPath);
        Assert.Equal(3, _system.Transfers.Count);
    }

    [Fact]
    public void ARuleThatSkipsLeavesTheFileWhenItsNameIsTaken()
    {
        _system.AddFile(@"D:\Invoices\Invoice.pdf", size: 5);

        var entry = Act(Rule("Move", ifExists: "Skip"))!;

        Assert.Equal(HistoryOutcome.Skipped, entry.Outcome);
        Assert.Equal(SkipReason.NameTaken, entry.Reason);
        Assert.True(_system.Exists(Invoice));
        Assert.Equal(5, _system.Read(@"D:\Invoices\Invoice.pdf")!.Size);
    }

    [Fact]
    public void ADestinationThatDoesntExistIsntCreated()
    {
        var entry = Act(Rule("Move", @"D:\Receipts\{year}"))!;

        Assert.Equal(SkipReason.DestinationNotFound, entry.Reason);
        Assert.False(_system.Exists(@"D:\Receipts"));
        Assert.True(_system.Exists(Invoice));
    }

    [Fact]
    public void ADestinationReachedThroughALinkIsntUsed()
    {
        _system.AddLink(@"D:\Linked");

        var entry = Act(Rule("Move", @"D:\Linked\Invoices"))!;

        Assert.Equal(SkipReason.Naming, entry.Reason);
        Assert.Equal(NamingProblem.DestinationNotAllowed, entry.Naming);
        Assert.True(_system.Exists(Invoice));
    }

    [Fact]
    public void ADestinationThatEndsUpInTheOwnFolderIsntUsed()
    {
        _system.AddFile(@"D:\Downloads\README");

        var entry = Act(Rule("Move", @"D:\Downloads\{extension}"), @"D:\Downloads\README")!;

        Assert.Equal(NamingProblem.DestinationIsOwnFolder, entry.Naming);
        Assert.Empty(_system.Transfers);
    }

    [Fact]
    public void ANewNameWindowsDoesntAllowLeavesTheFile()
    {
        _system.AddFile(@"D:\Downloads\README");

        var entry = Act(Rule("Rename", newName: "{name} {extension}"), @"D:\Downloads\README")!;

        Assert.Equal(SkipReason.Naming, entry.Reason);
        Assert.Equal(NamingProblem.InvalidName, entry.Naming);
        Assert.Empty(_system.Transfers);
    }

    [Fact]
    public void ARenameKeepsTheFileInItsFolder()
    {
        var entry = Act(Rule("Rename", newName: "{date} {name}"))!;

        Assert.Equal(HistoryOutcome.Renamed, entry.Outcome);
        Assert.Equal(@"D:\Downloads\2026-03-05 Invoice.pdf", entry.NewPath);
        Assert.False(_system.Exists(Invoice));
    }

    [Fact]
    public void ARenameToTheNameTheFileHasDoesNothing()
    {
        Assert.Null(Act(Rule("Rename", newName: "{name}")));
        Assert.Empty(_system.Transfers);
    }

    [Fact]
    public void ARenameMayChangeOnlyTheCase()
    {
        var entry = Act(Rule("Rename", newName: "INVOICE", ifExists: "Skip"))!;

        Assert.Equal(HistoryOutcome.Renamed, entry.Outcome);
        Assert.Equal(@"D:\Downloads\INVOICE.pdf", entry.NewPath);
    }

    [Fact]
    public void ARenameToATakenNameGetsTheNextNumber()
    {
        _system.AddFile(@"D:\Downloads\Paid Invoice.pdf");

        var entry = Act(Rule("Rename", newName: "Paid {name}"))!;

        Assert.Equal(@"D:\Downloads\Paid Invoice (2).pdf", entry.NewPath);
    }

    [Fact]
    public void AFileInUseIsLeftWhereItIs()
    {
        _system.SetBusy(Invoice, busy: true);

        var entry = Act(Rule("Move"))!;

        Assert.Equal(SkipReason.InUse, entry.Reason);
        Assert.True(_system.Exists(Invoice));
    }

    [Fact]
    public void WindowsRefusalIsInTheHistoryWithItsCode()
    {
        _system.Deny(@"D:\Invoices");

        var entry = Act(Rule("Move"))!;

        Assert.Equal(SkipReason.Failed, entry.Reason);
        Assert.Equal(5, entry.ErrorCode);
    }

    [Fact]
    public void ASubfolderWindowsWontCreateLeavesTheFile()
    {
        _system.Deny(@"D:\Invoices");

        var entry = Act(Rule("Copy", @"D:\Invoices\{year}"))!;

        Assert.Equal(SkipReason.Failed, entry.Reason);
        Assert.Equal(5, entry.ErrorCode);
        Assert.False(_system.Exists(@"D:\Invoices\2026"));
    }

    [Fact]
    public void AFileWithAMarkIsntMovedToADriveThatCantKeepIt()
    {
        _system.AddFile(@"D:\Downloads\setup.pdf", mark: "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/setup.pdf\r\n");
        _system.AddFolder(@"E:\Card");
        _system.LoseStreams(@"E:\");

        var entry = Act(Rule("Move", @"E:\Card\{year}"), @"D:\Downloads\setup.pdf")!;

        Assert.Equal(SkipReason.MarkWouldBeLost, entry.Reason);
        Assert.True(_system.Exists(@"D:\Downloads\setup.pdf"));
        Assert.False(_system.Exists(@"E:\Card\2026"));
        Assert.Empty(_system.Transfers);
    }

    [Fact]
    public void AFileWithoutAMarkMovesToADriveThatCantKeepOne()
    {
        _system.AddFolder(@"E:\Card");
        _system.LoseStreams(@"E:\");

        Assert.Equal(HistoryOutcome.Copied, Act(Rule("Copy", @"E:\Card"))!.Outcome);
    }

    [Fact]
    public void ARefusalInAFolderWindowsProtectsIsABlock()
    {
        _system.AddFolder(@"C:\Users\Ana\Documents\Invoices");
        _system.Deny(@"C:\Users\Ana\Documents\Invoices");

        var entry = Act(Rule("Move", @"C:\Users\Ana\Documents\Invoices"))!;

        Assert.Equal(SkipReason.Blocked, entry.Reason);
        Assert.Equal(5, entry.ErrorCode);
        Assert.True(_system.Exists(Invoice));
    }

    [Fact]
    public void TheCurrentUsersProtectedFoldersAreKnown()
    {
        var places = SpecialFolders.ForCurrentUser(@"C:\Data");

        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), places.Protected);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), places.Protected);
    }

    [Fact]
    public void AFileThatWentAwayIsReported()
    {
        var file = _system.Read(Invoice)!;
        _system.Move(Invoice, @"D:\Elsewhere.pdf", CancellationToken.None);

        var entry = _actions.Act(Downloads, RuleMatcher.Create(Rule("Move"), 1), file, FakeFolderRulesSystem.Time, CancellationToken.None)!;

        Assert.Equal(SkipReason.Changed, entry.Reason);
    }
}
