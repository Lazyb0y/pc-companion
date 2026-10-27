using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

// The feature's thread uses one window class name, so its tests run one at a time.
[CollectionDefinition(nameof(FolderRulesThreads))]
public sealed class FolderRulesThreads;

// Each test runs the feature's thread with its own hidden window and a fake file system, and a
// clock that moves only when the test moves it, so no file is touched.
[Collection(nameof(FolderRulesThreads))]
public sealed class FolderRulesThreadTests : IDisposable
{
    private const string Downloads = @"D:\Downloads";
    private const string Sorted = @"D:\Sorted";

    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", [@"C:\Users\Ana\AppData"]);

    private readonly FakeFolderRulesSystem _system = new FakeFolderRulesSystem().AddFolder(Downloads).AddFolder(Sorted);
    private readonly ManualTimeProvider _time = new() { Now = FakeFolderRulesSystem.Time.AddDays(1) };
    private readonly RecordingLogger<FolderRulesThread> _logger = new();
    private readonly TemporaryFolder _data = new();
    private readonly HistoryStore _store;
    private FolderRulesThread? _thread;

    public FolderRulesThreadTests()
    {
        _store = new HistoryStore(Path.Combine(_data.FullPath, HistoryStore.FileName));
    }

    private static FolderRulesSettings MovePdfs(string folder = Downloads) => new()
    {
        Enabled = true,
        Folders = [new WatchedFolder { Path = folder, Rules = [new FolderRule { Name = "PDFs", Extensions = ["pdf"], Destination = Sorted }] }],
    };

    public void Dispose()
    {
        if (_thread is not null)
        {
            _thread.Stop();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
        }

        _data.Dispose();
    }

    private void Start(FolderRulesSettings settings)
    {
        _thread = new FolderRulesThread(settings, _system, _store, Places, _time, _logger);
        Assert.True(_system.WaitUntil(system => settings.Folders.All(folder => system.IsWatching(folder.Path))));
    }

    // Moves the clock on until the condition holds, while the thread's timer checks the files.
    private bool AdvanceUntil(Func<FakeFolderRulesSystem, bool> condition, int seconds = 40)
    {
        for (var i = 0; i < seconds; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            if (_system.WaitUntil(condition, TimeSpan.FromMilliseconds(250)))
            {
                return true;
            }
        }

        return false;
    }

    private bool Logged(string message) => _logger.Entries.Any(entry => entry.Message == message);

    private FolderRulesHistory History()
    {
        _thread!.Stop();
        Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
        _thread = null;
        return _store.Load(out _);
    }

    [Fact]
    public void AFileThatArrivesIsMovedByItsRule()
    {
        Start(MovePdfs());

        _system.Arrive(@"D:\Downloads\report.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));
        Assert.Contains("background", _system.Events);
        var entry = Assert.Single(History().Entries);
        Assert.Equal(HistoryOutcome.Moved, entry.Outcome);
        Assert.Equal(@"D:\Sorted\report.pdf", entry.NewPath);
        Assert.True(Logged("Folder Rules folder 1, rule 1: Moved."));
    }

    [Fact]
    public void AFileNoRuleMatchesStays()
    {
        Start(MovePdfs());

        _system.Arrive(@"D:\Downloads\notes.txt");
        _system.Arrive(@"D:\Downloads\report.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));
        Assert.True(_system.Exists(@"D:\Downloads\notes.txt"));
        Assert.Single(History().Entries);
    }

    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    [InlineData(FileAttributes.Offline | FileAttributes.ReparsePoint)]
    public void FilesTheRulesNeverTouchStay(FileAttributes attributes)
    {
        Start(MovePdfs());

        _system.Arrive(@"D:\Downloads\left.pdf", attributes: attributes);
        _system.Arrive(@"D:\Downloads\report.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));
        Assert.True(_system.Exists(@"D:\Downloads\left.pdf"));
    }

    [Fact]
    public void FilesThatArrivedWhilePCCompanionWasntRunningAreHandledAtStart()
    {
        var lastLooked = FakeFolderRulesSystem.Time.AddHours(1);
        var history = new FolderRulesHistory();
        history.SetLastLooked(Downloads, lastLooked);
        _store.Save(history);
        _system.AddFile(@"D:\Downloads\old.pdf", created: lastLooked.AddMinutes(-5));
        _system.AddFile(@"D:\Downloads\new.pdf", created: lastLooked.AddMinutes(5));

        Start(MovePdfs());

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\new.pdf")));
        Assert.True(_system.Exists(@"D:\Downloads\old.pdf"));
        Assert.True(Logged("Folder Rules looked at folder 1: 1 of 2 files arrived since it last looked."));
    }

    [Fact]
    public void AFolderTheRulesNeverLookedAtKeepsItsFiles()
    {
        _system.AddFile(@"D:\Downloads\there.pdf", created: _time.Now.AddMinutes(-1));

        Start(MovePdfs());
        _system.Arrive(@"D:\Downloads\report.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));
        Assert.True(_system.Exists(@"D:\Downloads\there.pdf"));
        Assert.NotNull(History().LastLookedAt(Downloads));
    }

    [Fact]
    public void AFolderTurnedOnLaterKeepsItsFiles()
    {
        const string Scans = @"D:\Scans";
        var history = new FolderRulesHistory();
        history.SetLastLooked(Scans, FakeFolderRulesSystem.Time);
        _store.Save(history);
        _system.AddFile(@"D:\Scans\scan.pdf", created: FakeFolderRulesSystem.Time.AddHours(1));
        Start(MovePdfs());

        var both = MovePdfs() with { Folders = [.. MovePdfs().Folders, .. MovePdfs(Scans).Folders] };
        _thread!.Update(both);
        Assert.True(_system.WaitUntil(system => system.IsWatching(Scans)));
        _system.Arrive(@"D:\Scans\new.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\new.pdf")));
        Assert.True(_system.Exists(@"D:\Scans\scan.pdf"));
    }

    [Fact]
    public void LostChangesStartTheWatchAgainAndLookAtTheWholeFolder()
    {
        Start(MovePdfs());
        _system.AddFile(@"D:\Downloads\missed.pdf", created: _time.Now.AddMinutes(1));

        _system.LoseChanges(Downloads);

        Assert.True(_system.WaitUntil(system => system.Events.Count(happened => happened == $"watch {Downloads}") == 2));
        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\missed.pdf")));
        Assert.True(Logged("Folder Rules' watch of folder 1 stopped (lost changes: True), so it starts again and looks at the whole folder."));
    }

    [Fact]
    public void AFileTheRulesHandledIsLeftWhenItComesBack()
    {
        Start(MovePdfs());
        _system.Arrive(@"D:\Downloads\report.pdf");
        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));

        _system.Move(@"D:\Sorted\report.pdf", @"D:\Downloads\report.pdf", TestContext.Current.CancellationToken);
        _system.Report(@"D:\Downloads\report.pdf");

        Assert.False(AdvanceUntil(system => system.Transfers.Count > 2, seconds: 8));
        Assert.True(Logged("Folder Rules folder 1 already handled a file that came back, so it's left as it is."));
    }

    [Fact]
    public void AFileInUseForFiveMinutesStaysWithAnEntry()
    {
        Start(MovePdfs());
        _system.SetBusy(@"D:\Downloads\report.pdf", busy: true);
        _system.Arrive(@"D:\Downloads\report.pdf");

        for (var i = 0; i < 40 && !Logged("Folder Rules folder 1, rule 1 left a file where it is: InUse."); i++)
        {
            _time.Advance(TimeSpan.FromSeconds(30));
            _system.WaitUntil(_ => false, TimeSpan.FromMilliseconds(300));
        }

        Assert.True(Logged("Folder Rules folder 1, rule 1 left a file where it is: InUse."));
        Assert.True(_system.Exists(@"D:\Downloads\report.pdf"));
    }

    private static FolderRulesSettings RecycleLogs() => new()
    {
        Enabled = true,
        Folders =
        [
            new WatchedFolder
            {
                Path = Downloads,
                Rules =
                [
                    new FolderRule { Name = "Logs", Extensions = ["log"], Action = "RecycleBin" },
                    new FolderRule { Name = "PDFs", Extensions = ["pdf"], Destination = Sorted },
                ],
            },
        ],
    };

    // The history the thread saved, read while it runs.
    private FolderRulesHistory Saved()
    {
        for (var i = 0; i < 20; i++)
        {
            var history = _store.Load(out var readable);
            if (readable)
            {
                return history;
            }

            _system.WaitUntil(_ => false, TimeSpan.FromMilliseconds(50));
        }

        throw new InvalidOperationException("The history couldn't be read.");
    }

    [Fact]
    public void AFileARecycleBinRuleMatchesGoesToTheRecycleBin()
    {
        Start(RecycleLogs());

        _system.Arrive(@"D:\Downloads\old.log");

        Assert.True(AdvanceUntil(system => !system.Exists(@"D:\Downloads\old.log")));
        Assert.Contains(@"recycle D:\Downloads\old.log", _system.Events);
        var entry = Assert.Single(History().Entries);
        Assert.Equal(HistoryOutcome.MovedToRecycleBin, entry.Outcome);
        Assert.True(_system.RecycleBin!.IsDisposed);
    }

    [Fact]
    public void WhileWindowsAsksTheHistoryShowsTheFileWaitingAndTheOtherRulesGoOn()
    {
        _system.Asking.Add(@"D:\Downloads\huge.log");
        Start(RecycleLogs());

        _system.Arrive(@"D:\Downloads\huge.log");
        Assert.True(AdvanceUntil(_ => Logged("Folder Rules folder 1, rule 1: Windows asks whether to delete a file too big for the Recycle Bin.")));
        _system.Arrive(@"D:\Downloads\report.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));
        Assert.Contains(Saved().Entries, entry => entry.Name == "huge.log" && entry.Outcome == HistoryOutcome.WaitingForAnswer);

        _system.RecycleBin!.Answer(@"D:\Downloads\huge.log", RecycleOutcome.Kept);

        Assert.True(_system.WaitUntil(_ => Logged("Folder Rules folder 1, rule 1: KeptByAnswer.")));
        var entries = History().Entries;
        Assert.Equal(2, entries.Count);
        var kept = Assert.Single(entries, entry => entry.Name == "huge.log");
        Assert.Equal(HistoryOutcome.KeptByAnswer, kept.Outcome);
        Assert.Equal(1, kept.Id);
        Assert.True(_system.Exists(@"D:\Downloads\huge.log"));
    }

    [Theory]
    [InlineData(RecycleOutcome.NoRecycleBin, HistoryOutcome.Skipped, SkipReason.NoRecycleBin)]
    [InlineData(RecycleOutcome.DeletedForGood, HistoryOutcome.DeletedForGood, SkipReason.None)]
    public void TheHistorySaysWhatTheRecycleBinDid(RecycleOutcome outcome, HistoryOutcome recorded, SkipReason reason)
    {
        _system.RecycleAnswer = new RecycleResult(outcome);
        Start(RecycleLogs());

        _system.Arrive(@"D:\Downloads\old.log");

        Assert.True(AdvanceUntil(system => system.Events.Contains(@"recycle D:\Downloads\old.log")));
        Assert.True(_system.WaitUntil(_ => _logger.Entries.Any(entry => entry.Message.StartsWith("Folder Rules folder 1, rule 1", StringComparison.Ordinal))));
        var entry = Assert.Single(History().Entries);
        Assert.Equal(recorded, entry.Outcome);
        Assert.Equal(reason, entry.Reason);
    }

    [Fact]
    public void AWindowsErrorFromTheRecycleBinIsKeptWithItsCode()
    {
        _system.RecycleAnswer = new RecycleResult(RecycleOutcome.Failed, unchecked((int)0x80070005));
        Start(RecycleLogs());

        _system.Arrive(@"D:\Downloads\old.log");

        Assert.True(AdvanceUntil(_ => Logged("Windows refused Folder Rules folder 1, rule 1 (error -2147024891).")));
        Assert.Equal(SkipReason.Failed, Assert.Single(History().Entries).Reason);
    }

    [Fact]
    public void AnUnusableRuleIsLeftOutWithAWarning()
    {
        var settings = MovePdfs();
        settings.Folders[0].Rules = [new FolderRule { Name = "Everything", Destination = Sorted }, .. settings.Folders[0].Rules];

        Start(settings);

        Assert.True(_system.WaitUntil(_ => Logged("Folder Rules folder 1, rule 1 can't be used: NoCondition.")));
        Assert.True(Logged("Folder Rules applied: 1 folders and 1 rules can be used."));
    }

    [Fact]
    public void AFolderThatCantBeWatchedIsReported()
    {
        _thread = new FolderRulesThread(MovePdfs(@"D:\Missing"), _system, _store, Places, _time, _logger);

        Assert.True(_system.WaitUntil(_ => Logged("Folder Rules can't watch folder 1 (error -2147024809).")));
    }

    [Fact]
    public void TurningAFolderOffStopsItsWatchAndForgetsItsLastLook()
    {
        Start(MovePdfs());

        _thread!.Update(MovePdfs() with { Folders = [] });

        Assert.True(_system.WaitUntil(system => !system.IsWatching(Downloads)));
        Assert.Null(History().LastLookedAt(Downloads));
    }

    [Fact]
    public void TheHistoryNeverLogsNamesOrPaths()
    {
        Start(MovePdfs());

        _system.Arrive(@"D:\Downloads\report.pdf");

        Assert.True(AdvanceUntil(system => system.Exists(@"D:\Sorted\report.pdf")));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("report", StringComparison.OrdinalIgnoreCase) || entry.Message.Contains(@"D:\", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }
}
