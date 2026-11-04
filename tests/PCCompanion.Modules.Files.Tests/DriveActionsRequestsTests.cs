using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>The settings app's requests, as the runner answers them.</summary>
public sealed class DriveActionsRequestsTests : IDisposable
{
    private const string Id = "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f";
    private const uint Serial = 0xA8D4F2F8;
    private const string HardwareSerial = "0LVF0HQ5";
    private const string Documents = @"C:\Users\Ana\Documents";
    private const string Pictures = @"C:\Users\Ana\Pictures";
    private const string FirstPhoto = @"100CANON\IMG_0001.JPG";

    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", []);

    private readonly TemporaryFolder _data = new();
    private readonly FakeDriveActionsSystem _system = new();
    private readonly FakeDriveActionsHost _host = new();
    private readonly ManualTimeProvider _time = new() { Now = FakeDriveActionsSystem.Taken.AddDays(5) };
    private readonly DriveRecordsStore _records;
    private readonly DriveActionsRunner _runner;

    public DriveActionsRequestsTests()
    {
        _system.AddFolder(Pictures);
        _records = new DriveRecordsStore(Path.Join(_data.FullPath, DriveRecordsStore.FolderName));
        _runner = new DriveActionsRunner(
            _system,
            _host,
            _records,
            new DriveActionsHistoryStore(Path.Join(_data.FullPath, DriveActionsHistoryStore.FileName)),
            Places,
            _time,
            NullLogger.Instance,
            TimeZoneInfo.Utc);
    }

    private static DriveEntry Cards(string whenConnected = "Nothing") => new()
    {
        Name = "Camera cards",
        Kind = "CameraCards",
        WhenConnected = whenConnected,
        Actions = [new DriveAction { Action = "Import", From = "DCIM", Extensions = ["jpg", "cr3"], Destination = Pictures + @"\{year}\{month}" }],
    };

    private static DriveEntry Backup(string whenConnected = "Nothing") => new()
    {
        Name = "Backup drive",
        Id = Id,
        VolumeSerial = DriveIdentity.HashVolumeSerial(Serial),
        HardwareSerial = DriveIdentity.HashHardwareSerial(HardwareSerial),
        WhenConnected = whenConnected,
        Actions = [new DriveAction { Action = "BackUp", Folders = [Documents], Destination = "Backup" }],
    };

    [Fact]
    public void DrivesListsTheConnectedDrivesThatCount()
    {
        Apply(Backup());
        AddBackupDrive('E');
        AddCard('F');
        _system.AddDrive('D', label: "DATA", removable: false, bus: 11, ejectable: false);
        Arrive('D', 'E', 'F');

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Drives });

        Assert.Equal(["E", "F"], answer.Drives.Select(drive => drive.Letter));
        Assert.Equal((RecognitionOutcome.Recognized, 0, AddProblem.AlreadyAdded), (answer.Drives[0].Outcome, answer.Drives[0].Drive, answer.Drives[0].AddProblem));
        Assert.Equal((RecognitionOutcome.NotRecognized, -1, AddProblem.None), (answer.Drives[1].Outcome, answer.Drives[1].Drive, answer.Drives[1].AddProblem));
        Assert.Equal("EOS_DIGITAL", answer.Drives[1].Label);
    }

    [Fact]
    public void ADrivesKeyIsGivenWithIt()
    {
        Apply(Cards(), Backup());
        AddBackupDrive('E');
        AddCard('F');
        Arrive('E', 'F');

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Drives });

        Assert.Equal([Id, DriveRecordsStore.CameraCardsKey], answer.Drives.Select(drive => drive.Key));
    }

    [Fact]
    public void ADriveWhoseIdFileIsGoneIsTheListsDriveWithItsSerials()
    {
        Apply(Backup());
        _system.AddDrive('E', serial: Serial, hardwareSerial: HardwareSerial);
        _system.AddDrive('F', serial: 0x12345678, hardwareSerial: HardwareSerial);
        Arrive('E', 'F');

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Drives });

        Assert.Equal((RecognitionOutcome.NotRecognized, 0, Id), (answer.Drives[0].Outcome, answer.Drives[0].Drive, answer.Drives[0].Key));
        Assert.Equal((RecognitionOutcome.NotRecognized, -1, ""), (answer.Drives[1].Outcome, answer.Drives[1].Drive, answer.Drives[1].Key));
    }

    [Fact]
    public void ALockedDriveIsTheListsDriveWithItsHardwareSerial()
    {
        Apply(Backup());
        _system.AddDrive('E', hardwareSerial: HardwareSerial, locked: true);
        _system.AddDrive('F', hardwareSerial: null, locked: true);
        Arrive('E', 'F');

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Drives });

        Assert.Equal((RecognitionOutcome.Locked, 0), (answer.Drives[0].Outcome, answer.Drives[0].Drive));
        Assert.Equal((RecognitionOutcome.Locked, -1), (answer.Drives[1].Outcome, answer.Drives[1].Drive));
    }

    [Fact]
    public void AddingADriveWritesItsIdFileAndGivesItsEntry()
    {
        Apply();
        _system.AddDrive('E', label: "BACKUP", serial: Serial, hardwareSerial: HardwareSerial);
        Arrive('E');

        var answer = Ask(Request(DriveActionsRequestKind.AddDrive, 'E'));

        Assert.Equal(DriveActionsAnswerStatus.Done, answer.Status);
        var entry = Assert.IsType<DriveEntry>(answer.Entry);
        var idFile = _system.FileAt(@"E:\" + DriveIdentity.IdFileName);
        Assert.NotNull(idFile);
        Assert.Equal(entry.Id, idFile.Text);
        Assert.True(DriveIdentity.IsId(entry.Id));
        Assert.Equal(("BACKUP", DriveIdentity.HashVolumeSerial(Serial), DriveIdentity.HashHardwareSerial(HardwareSerial)), (entry.Name, entry.VolumeSerial, entry.HardwareSerial));
    }

    [Fact]
    public void ADriveWithoutALabelGetsTheNameTheSettingsAppGives()
    {
        Apply();
        _system.AddDrive('E', label: "", serial: Serial, hardwareSerial: HardwareSerial);
        Arrive('E');

        var answer = Ask(Request(DriveActionsRequestKind.AddDrive, 'E') with { Name = "  USB drive  " });

        Assert.Equal("USB drive", answer.Entry!.Name);
    }

    [Fact]
    public void ADriveAddedOnAnotherPCKeepsItsIdFile()
    {
        Apply();
        AddBackupDrive('E');
        Arrive('E');
        var copies = _system.Copies.Count;

        var answer = Ask(Request(DriveActionsRequestKind.AddDrive, 'E'));

        Assert.Equal(Id, answer.Entry!.Id);
        Assert.Equal(Id, _system.FileAt(@"E:\" + DriveIdentity.IdFileName)!.Text);
        Assert.Equal(copies, _system.Copies.Count);
    }

    [Fact]
    public void ADriveInTheListAlreadyIsntAddedAgain()
    {
        Apply(Backup());
        AddBackupDrive('E');
        Arrive('E');

        var answer = Ask(Request(DriveActionsRequestKind.AddDrive, 'E'));

        Assert.Equal((DriveActionsAnswerStatus.CantAdd, AddProblem.AlreadyAdded), (answer.Status, answer.AddProblem));
        Assert.Null(answer.Entry);
    }

    [Fact]
    public void AnIdFileWindowsDoesntWriteFailsTheAdd()
    {
        Apply();
        _system.AddDrive('E', serial: Serial, hardwareSerial: HardwareSerial);
        _system.IdFileError = 5;
        Arrive('E');

        var answer = Ask(Request(DriveActionsRequestKind.AddDrive, 'E'));

        Assert.Equal((DriveActionsAnswerStatus.Failed, 5), (answer.Status, answer.ErrorCode));
        Assert.Null(answer.Entry);
    }

    [Theory]
    [InlineData("E", @"\\?\Volume{F}\")]
    [InlineData("G", @"\\?\Volume{G}\")]
    [InlineData("", "")]
    [InlineData("EF", @"\\?\Volume{E}\")]
    public void ARequestForADriveThatIsntConnectedIsRefused(string letter, string volume)
    {
        Apply();
        _system.AddDrive('E', serial: Serial, hardwareSerial: HardwareSerial);
        Arrive('E');

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.AddDrive, Letter = letter, Volume = volume });

        Assert.Equal(DriveActionsAnswerStatus.NotConnected, answer.Status);
        Assert.False(_system.Exists(@"E:\" + DriveIdentity.IdFileName));
    }

    [Fact]
    public void UseThisDriveGivesTheEntryWithTheLookAlikesSerials()
    {
        Apply(Backup());
        AddBackupDrive('E', serial: 0x12345678);
        Arrive('E');

        var answer = Ask(Request(DriveActionsRequestKind.UseDrive, 'E') with { Key = Id });

        var entry = Assert.IsType<DriveEntry>(answer.Entry);
        Assert.Equal((Id, "Backup drive", DriveIdentity.HashVolumeSerial(0x12345678)), (entry.Id, entry.Name, entry.VolumeSerial));
    }

    [Fact]
    public void UseThisDriveIsOnlyForALookAlikeOfTheDriveItNames()
    {
        Apply(Backup());
        AddBackupDrive('E');
        AddBackupDrive('F', serial: 0x12345678);
        Arrive('E', 'F');

        Assert.Equal(DriveActionsAnswerStatus.Refused, Ask(Request(DriveActionsRequestKind.UseDrive, 'E') with { Key = Id }).Status);
        Assert.Equal(DriveActionsAnswerStatus.Refused, Ask(Request(DriveActionsRequestKind.UseDrive, 'F') with { Key = "0123456789abcdef0123456789abcdef" }).Status);
    }

    [Fact]
    public void RunNowListsTheFilesAnImportWouldCopyAndThoseImportedBefore()
    {
        Apply(Cards());
        AddCard('F');
        Arrive('F');
        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with { Key = DriveRecordsStore.CameraCardsKey });
        _system.AddFile(@"F:\DCIM\100CANON\IMG_0003.JPG");

        var answer = Ask(Request(DriveActionsRequestKind.RunNowList, 'F') with { Key = DriveRecordsStore.CameraCardsKey });

        var preview = Assert.Single(answer.Actions);
        Assert.Equal(0, preview.Action);
        Assert.Equal(
            [(FirstPhoto, true), (@"100CANON\IMG_0003.JPG", false), (@"101CANON\IMG_0002.CR3", true)],
            preview.Files.Select(file => (file.Path, file.ImportedBefore)));
        Assert.All(preview.Files, file => Assert.Equal(Pictures + @"\2026\10", file.Destination));
    }

    [Fact]
    public void RunNowListForABackupCountsTheCopiesAndLeavesTheDriveAsItIs()
    {
        Apply(Backup());
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt", size: 300);
        _system.AddFile(Documents + @"\Work\plan.docx", size: 700);
        _system.AddFile(@"E:\Backup\Documents\notes.txt" + DriveFiles.TemporarySuffix, size: 100);
        Arrive('E');

        var answer = Ask(Request(DriveActionsRequestKind.RunNowList, 'E') with { Key = Id });

        var preview = Assert.Single(answer.Actions);
        Assert.Equal((2, 1000L, 0), (preview.Copies, preview.Bytes, preview.Skipped));
        Assert.True(_system.Exists(@"E:\Backup\Documents\notes.txt" + DriveFiles.TemporarySuffix));
        Assert.Empty(_system.Copies);
    }

    [Fact]
    public void RunNowCopiesOnlyTheChosenFilesAlsoOnesImportedBefore()
    {
        Apply(Cards());
        AddCard('F');
        Arrive('F');
        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with { Key = DriveRecordsStore.CameraCardsKey });
        var copies = _system.Copies.Count;

        var answer = Ask(Request(DriveActionsRequestKind.RunNow, 'F') with
        {
            Key = DriveRecordsStore.CameraCardsKey,
            Chosen = [new ChosenImport { Action = 0, Files = [FirstPhoto.ToLowerInvariant()] }],
        });

        Assert.Equal(DriveActionsAnswerStatus.Done, answer.Status);
        Assert.Equal(copies + 1, _system.Copies.Count);
        Assert.True(_system.Exists(Pictures + @"\2026\10\IMG_0001 (2).JPG"));
        Assert.False(_system.Exists(Pictures + @"\2026\10\IMG_0002 (2).CR3"));
    }

    [Fact]
    public void RunNowCopiesNothingThatIsntInTheList()
    {
        Apply(Cards());
        AddCard('F');
        _system.AddFile(@"F:\private.jpg");
        Arrive('F');

        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with
        {
            Key = DriveRecordsStore.CameraCardsKey,
            Chosen = [new ChosenImport { Action = 0, Files = [@"..\private.jpg", @"100CANON\clip.mov"] }],
        });

        Assert.Empty(_system.Copies);
    }

    [Fact]
    public void AnImportNotInRunNowsChoiceCopiesItsNewFiles()
    {
        Apply(Cards() with { Actions = [.. Cards().Actions, Cards().Actions[0] with { Extensions = ["mov"] }] });
        AddCard('F');
        Arrive('F');

        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with
        {
            Key = DriveRecordsStore.CameraCardsKey,
            Chosen = [new ChosenImport { Action = 0, Files = [FirstPhoto] }],
        });

        Assert.Equal(
            [Pictures + @"\2026\10\IMG_0001.JPG", Pictures + @"\2026\10\clip.mov"],
            _system.Copies.Select(copy => copy.Destination));
    }

    [Fact]
    public void RunNowWithAnActionThatHasAProblemKeepsTheOthersNumbers()
    {
        var cards = Cards();
        Apply(cards with { Actions = [cards.Actions[0] with { Destination = @"C:\Windows\Photos" }, cards.Actions[0]] });
        AddCard('F');
        Arrive('F');

        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with
        {
            Key = DriveRecordsStore.CameraCardsKey,
            Chosen = [new ChosenImport { Action = 1, Files = [FirstPhoto] }],
        });

        Assert.Equal([Pictures + @"\2026\10\IMG_0001.JPG"], _system.Copies.Select(copy => copy.Destination));
    }

    [Fact]
    public void RunNowForADriveThatsRunningOrWhileThePCIsLockedIsRefused()
    {
        Apply(Cards());
        AddCard('F');
        Arrive('F');
        var request = Request(DriveActionsRequestKind.RunNow, 'F') with { Key = DriveRecordsStore.CameraCardsKey };

        DriveActionsAnswer? first = null;
        DriveActionsAnswer? second = null;
        _runner.Answer(request, answer => first = answer);
        _runner.Answer(request, answer => second = answer);
        _host.RunAll();
        _runner.SessionLocked();
        var locked = Ask(request);

        Assert.Equal(DriveActionsAnswerStatus.Done, first?.Status);
        Assert.Equal(DriveActionsAnswerStatus.Refused, second?.Status);
        Assert.Equal(DriveActionsAnswerStatus.Refused, locked.Status);
        Assert.Equal(2, _system.Copies.Count);
    }

    [Fact]
    public void RunNowForAnotherDriveOfTheListIsRefused()
    {
        Apply(Cards());
        AddCard('F');
        Arrive('F');

        var answer = Ask(Request(DriveActionsRequestKind.RunNow, 'F') with { Key = Id });

        Assert.Equal(DriveActionsAnswerStatus.NotConnected, answer.Status);
        Assert.Empty(_system.Copies);
    }

    [Fact]
    public void HistoryIsNewestFirstAndClearHistoryEmptiesIt()
    {
        Apply(Cards());
        AddCard('F');
        Arrive('F');
        var request = Request(DriveActionsRequestKind.RunNow, 'F') with { Key = DriveRecordsStore.CameraCardsKey };
        Ask(request);
        _time.Now += TimeSpan.FromMinutes(1);
        Ask(request);

        var history = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.History });
        Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.ClearHistory });

        Assert.Equal(2, history.Runs.Count);
        Assert.True(history.Runs[0].Started > history.Runs[1].Started);
        Assert.Empty(Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.History }).Runs);
    }

    [Fact]
    public void UndoSendsAnImportsCopiesToTheRecycleBin()
    {
        var run = ImportedRun();

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started });

        Assert.Equal((2, 0), (answer.Undone, answer.NotUndone));
        Assert.Equal(2, _system.Recycled.Count);
        Assert.False(_system.Exists(Pictures + @"\2026\10\IMG_0001.JPG"));
        Assert.True(_system.Exists(@"F:\DCIM\100CANON\IMG_0001.JPG"));
        var undone = Assert.Single(Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.History }).Runs);
        Assert.All(undone.Actions[0].Files, file => Assert.True(file.Undone));
    }

    [Fact]
    public void UndoOfOneFileLeavesTheOthers()
    {
        var run = ImportedRun();

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started, File = FirstPhoto });

        Assert.Equal(1, answer.Undone);
        Assert.False(_system.Exists(Pictures + @"\2026\10\IMG_0001.JPG"));
        Assert.True(_system.Exists(Pictures + @"\2026\10\IMG_0002.CR3"));
    }

    [Fact]
    public void UndoLeavesACopyThatChangedSince()
    {
        var run = ImportedRun();
        _system.AddFile(Pictures + @"\2026\10\IMG_0001.JPG", size: 5000);

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started });

        Assert.Equal((1, 1), (answer.Undone, answer.NotUndone));
        Assert.True(_system.Exists(Pictures + @"\2026\10\IMG_0001.JPG"));
    }

    [Fact]
    public void ACopyUndoneOnceIsntUndoneAgainAndStillCountsAsImported()
    {
        var run = ImportedRun();
        var undo = new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started };
        Ask(undo);

        Assert.Equal(DriveActionsAnswerStatus.Refused, Ask(undo).Status);
        var preview = Ask(Request(DriveActionsRequestKind.RunNowList, 'F') with { Key = DriveRecordsStore.CameraCardsKey });
        Assert.All(preview.Actions[0].Files, file => Assert.True(file.ImportedBefore));
    }

    [Fact]
    public void UndoOfARunThatIsntInTheHistoryIsRefused()
    {
        var run = ImportedRun();

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started.AddSeconds(1) });

        Assert.Equal(DriveActionsAnswerStatus.Refused, answer.Status);
        Assert.Empty(_system.Recycled);
    }

    [Fact]
    public void UndoDoesntTouchABackupsCopies()
    {
        Apply(Backup());
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt");
        Arrive('E');
        Ask(Request(DriveActionsRequestKind.RunNow, 'E') with { Key = Id });
        var run = Assert.Single(Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.History }).Runs);

        var answer = Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started });

        Assert.Equal(DriveActionsAnswerStatus.Refused, answer.Status);
        Assert.True(_system.Exists(@"E:\Backup\Documents\notes.txt"));
    }

    [Fact]
    public void TheRecycleBinEndsWithTheRunner()
    {
        var run = ImportedRun();
        Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started });

        _runner.Close();

        Assert.True(_system.RecycleBinDisposed);
    }

    [Fact]
    public void TheRecordsOfADriveRemovedFromTheListAreDeleted()
    {
        Apply(Backup(), Cards());
        AddBackupDrive('E');
        AddCard('F');
        _system.AddFile(Documents + @"\notes.txt");
        Arrive('E', 'F');
        Ask(Request(DriveActionsRequestKind.RunNow, 'E') with { Key = Id });
        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with { Key = DriveRecordsStore.CameraCardsKey });
        Assert.True(File.Exists(Path.Join(_records.FolderPath, Id + ".json")));

        Apply(Backup());

        Assert.True(File.Exists(Path.Join(_records.FolderPath, Id + ".json")));
        Assert.False(File.Exists(Path.Join(_records.FolderPath, DriveRecordsStore.CameraCardsKey + ".json")));
    }

    public void Dispose() => _data.Dispose();

    private static DriveActionsRequest Request(DriveActionsRequestKind kind, char letter) =>
        new() { Kind = kind, Letter = letter.ToString(), Volume = $@"\\?\Volume{{{letter}}}\" };

    // Answers the request, and runs what it starts until there's nothing left, as the thread would.
    private DriveActionsAnswer Ask(DriveActionsRequest request)
    {
        DriveActionsAnswer? answer = null;
        _runner.Answer(request, reply => answer = reply);
        _host.RunAll();
        return Assert.IsType<DriveActionsAnswer>(answer);
    }

    // A card's photos imported by Run now, and the run in the history.
    private RunEntry ImportedRun()
    {
        Apply(Cards());
        AddCard('F');
        Arrive('F');
        Ask(Request(DriveActionsRequestKind.RunNow, 'F') with { Key = DriveRecordsStore.CameraCardsKey });
        return Assert.Single(Ask(new DriveActionsRequest { Kind = DriveActionsRequestKind.History }).Runs);
    }

    private void Apply(params DriveEntry[] drives) => _runner.Apply(new DriveActionsSettings { Enabled = true, Drives = drives });

    private void Arrive(params char[] letters)
    {
        foreach (var letter in letters)
        {
            _runner.Arrived(letter, connectedBefore: false);
        }

        _host.RunAll();
    }

    private void AddCard(char letter)
    {
        _system.AddDrive(letter, label: "EOS_DIGITAL", fileSystem: "exFAT", serial: 0x5AA18C1A, hardwareSerial: null, bus: 12);
        _system.AddFile($@"{letter}:\DCIM\100CANON\IMG_0001.JPG");
        _system.AddFile($@"{letter}:\DCIM\101CANON\IMG_0002.CR3");
        _system.AddFile($@"{letter}:\DCIM\100CANON\clip.mov");
    }

    private void AddBackupDrive(char letter, uint serial = Serial)
    {
        _system.AddDrive(letter, label: "BACKUP", serial: serial, hardwareSerial: HardwareSerial);
        _system.AddFile($@"{letter}:\{DriveIdentity.IdFileName}", size: 32, attributes: FileAttributes.Hidden | FileAttributes.System, text: Id);
    }
}
