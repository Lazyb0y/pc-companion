using System;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class FolderRulesHistoryTests
{
    private const string Downloads = @"C:\Users\Ana\Downloads";

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly FileIdentity Report = new("0123-00000000000000000001000000000abc", 1000, Now.AddDays(-1));

    private static HistoryEntry Moved(DateTimeOffset time) => new()
    {
        Time = time,
        Folder = Downloads,
        Rule = "PDFs",
        Name = "report.pdf",
        Outcome = HistoryOutcome.Moved,
        NewPath = @"D:\Documents\2026 report.pdf",
        File = Report,
    };

    [Fact]
    public void EntriesGetTheNextNumberAndTheNewestComesFirst()
    {
        var history = new FolderRulesHistory();

        var first = history.Add(Moved(Now));
        var second = history.Add(Moved(Now.AddMinutes(1)));

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal([2L, 1L], history.Entries.Select(entry => entry.Id));
        Assert.Equal(second, history.Find(2));
    }

    [Fact]
    public void AnEntryIsReplacedWhenWindowsQuestionIsAnswered()
    {
        var history = new FolderRulesHistory();
        var waiting = history.Add(Moved(Now) with { Outcome = HistoryOutcome.WaitingForAnswer, NewPath = string.Empty });

        history.Replace(waiting with { Outcome = HistoryOutcome.KeptByAnswer });

        Assert.Equal(HistoryOutcome.KeptByAnswer, Assert.Single(history.Entries).Outcome);
    }

    [Fact]
    public void AFileWithTheSameIdWasHandledWhateverItsName()
    {
        var history = new FolderRulesHistory();
        history.Add(Moved(Now));

        Assert.True(history.WasHandled(Downloads, "anything.pdf", Report with { Size = 5 }));
        Assert.False(history.WasHandled(Downloads, "report.pdf", Report with { FileId = "0123-ffff" }));
    }

    [Fact]
    public void WithoutFileIdsTheNameSizeAndTimeTell()
    {
        var history = new FolderRulesHistory();
        history.Add(Moved(Now) with { File = Report with { FileId = null } });
        var copy = Report with { FileId = "0456-0001" };

        Assert.True(history.WasHandled(Downloads, "REPORT.pdf", copy));
        Assert.True(history.WasHandled(Downloads, "2026 report.pdf", copy));
        Assert.False(history.WasHandled(Downloads, "other.pdf", copy));
        Assert.False(history.WasHandled(Downloads, "report.pdf", copy with { Size = 1001 }));
        Assert.False(history.WasHandled(Downloads, "report.pdf", copy with { Modified = Now }));
    }

    [Fact]
    public void AnotherFoldersRulesStillHandleTheFile()
    {
        var history = new FolderRulesHistory();
        history.Add(Moved(Now));

        Assert.False(history.WasHandled(@"D:\Documents", "2026 report.pdf", Report));
    }

    [Theory]
    [InlineData(HistoryOutcome.Skipped, false)]
    [InlineData(HistoryOutcome.WaitingForAnswer, false)]
    [InlineData(HistoryOutcome.KeptByAnswer, true)]
    [InlineData(HistoryOutcome.Renamed, true)]
    [InlineData(HistoryOutcome.Copied, true)]
    [InlineData(HistoryOutcome.MovedToRecycleBin, true)]
    public void OnlyFilesTheRulesActedOnWereHandled(HistoryOutcome outcome, bool handled)
    {
        var history = new FolderRulesHistory();
        history.Add(Moved(Now) with { Outcome = outcome });

        Assert.Equal(handled, history.WasHandled(Downloads, "report.pdf", Report));
    }

    [Fact]
    public void AFilePutBackWithUndoWasHandled()
    {
        var history = new FolderRulesHistory();
        var entry = history.Add(Moved(Now));
        history.Replace(entry with { Undone = Now.AddMinutes(5) });

        Assert.True(history.WasHandled(Downloads, "report.pdf", Report));
    }

    [Fact]
    public void EntriesOlderThanThirtyDaysAndBeyondTheLimitAreDropped()
    {
        var history = new FolderRulesHistory();
        history.Add(Moved(Now.AddDays(-31)));
        for (var i = 0; i < FolderRulesHistory.MaximumEntries + 5; i++)
        {
            history.Add(Moved(Now.AddSeconds(i - 2000)));
        }

        history.Prune(Now, [Downloads]);

        Assert.Equal(FolderRulesHistory.MaximumEntries, history.Entries.Count);
        Assert.Equal(FolderRulesHistory.MaximumEntries + 6, history.Entries[0].Id);
        Assert.All(history.Entries, entry => Assert.True(entry.Time > Now.AddDays(-30)));
    }

    [Fact]
    public void FoldersNoLongerWatchedAreForgotten()
    {
        var history = new FolderRulesHistory();
        history.SetLastLooked(Downloads, Now);
        history.SetLastLooked(@"D:\Scans", Now);

        history.Prune(Now, [@"c:\users\ana\downloads"]);

        Assert.Equal(Now, history.LastLookedAt(Downloads));
        Assert.Null(history.LastLookedAt(@"D:\Scans"));
    }

    [Fact]
    public void ClearingEmptiesTheHistoryButKeepsWhenTheFoldersWereLookedAt()
    {
        var history = new FolderRulesHistory();
        history.Add(Moved(Now));
        history.SetLastLooked(Downloads, Now);

        history.Clear();

        Assert.Empty(history.Entries);
        Assert.Equal(Now, history.LastLookedAt(Downloads));
    }

    [Fact]
    public void AFileArrivedWhenItWasCreatedOrMovedInSinceTheLastLook()
    {
        var look = Now.AddHours(-1);
        var before = look.AddDays(-1);
        var after = look.AddMinutes(1);

        Assert.True(FolderRulesHistory.ArrivedSince(after, after, look));
        Assert.True(FolderRulesHistory.ArrivedSince(before, after, look));
        Assert.True(FolderRulesHistory.ArrivedSince(after, DateTimeOffset.FromFileTime(0), look));
        Assert.False(FolderRulesHistory.ArrivedSince(before, before, look));
        Assert.False(FolderRulesHistory.ArrivedSince(before, DateTimeOffset.FromFileTime(0), look));
    }

    [Fact]
    public void NothingArrivedInAFolderTheRulesNeverLookedAt()
    {
        Assert.False(FolderRulesHistory.ArrivedSince(Now, Now, null));
    }

    [Fact]
    public void TheHistoryIsSavedAndReadBack()
    {
        using var folder = new TemporaryFolder();
        var store = new HistoryStore(Path.Combine(folder.FullPath, HistoryStore.FileName));
        var history = new FolderRulesHistory();
        history.Add(Moved(Now));
        history.Add(Moved(Now) with { Outcome = HistoryOutcome.Skipped, Reason = SkipReason.Naming, Naming = NamingProblem.InvalidName, File = null });
        history.SetLastLooked(Downloads, Now);

        store.Save(history);
        var loaded = store.Load(out var readable);

        Assert.True(readable);
        Assert.Equal(history.Entries, loaded.Entries);
        Assert.Equal(Now, loaded.LastLookedAt(Downloads));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void WithoutAFileTheHistoryIsEmpty()
    {
        using var folder = new TemporaryFolder();
        var store = new HistoryStore(Path.Combine(folder.FullPath, "Missing", HistoryStore.FileName));

        Assert.Empty(store.Load(out var readable).Entries);
        Assert.True(readable);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "entries": 5 }""")]
    [InlineData("""{ "entries": [ { "folder": null } ] }""")]
    public void AFileThatCantBeReadGivesAnEmptyHistory(string text)
    {
        using var folder = new TemporaryFolder();
        var store = new HistoryStore(Path.Combine(folder.FullPath, HistoryStore.FileName));
        File.WriteAllText(store.FilePath, text);

        Assert.Empty(store.Load(out var readable).Entries);
        Assert.False(readable);
    }

    [Fact]
    public void AFileThatsTooLargeIsntRead()
    {
        using var folder = new TemporaryFolder();
        var store = new HistoryStore(Path.Combine(folder.FullPath, HistoryStore.FileName));
        File.WriteAllText(store.FilePath, "{ \"entries\": [] }" + new string(' ', HistoryStore.MaximumFileSize));

        Assert.Empty(store.Load(out var readable).Entries);
        Assert.False(readable);
    }

    [Fact]
    public void ANullEntryIsLeftOut()
    {
        using var folder = new TemporaryFolder();
        var store = new HistoryStore(Path.Combine(folder.FullPath, HistoryStore.FileName));
        File.WriteAllText(store.FilePath, """{ "entries": [ null, { "id": 3, "folder": "C:\\Users\\Ana\\Downloads", "rule": "PDFs", "name": "a.pdf", "outcome": "Moved" } ] }""");

        var entry = Assert.Single(store.Load(out var readable).Entries);
        Assert.True(readable);
        Assert.Equal(3, entry.Id);
        Assert.Equal(string.Empty, entry.NewPath);
        Assert.Null(entry.File);
    }
}
