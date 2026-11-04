using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveActionsRunnerTests : IDisposable
{
    private const string Id = "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f";
    private const uint Serial = 0xA8D4F2F8;
    private const string HardwareSerial = "0LVF0HQ5";
    private const string Documents = @"C:\Users\Ana\Documents";
    private const string Pictures = @"C:\Users\Ana\Pictures";

    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", []);

    private readonly TemporaryFolder _data = new();
    private readonly FakeDriveActionsSystem _system = new();
    private readonly FakeDriveActionsHost _host = new();
    private readonly ManualTimeProvider _time = new() { Now = FakeDriveActionsSystem.Taken.AddDays(5) };
    private readonly DriveActionsRunner _runner;

    public DriveActionsRunnerTests()
    {
        _system.AddFolder(Pictures);
        _runner = new DriveActionsRunner(
            _system,
            _host,
            new DriveRecordsStore(Path.Join(_data.FullPath, DriveRecordsStore.FolderName)),
            new DriveActionsHistoryStore(Path.Join(_data.FullPath, DriveActionsHistoryStore.FileName)),
            Places,
            _time,
            NullLogger.Instance,
            TimeZoneInfo.Utc);
    }

    private static DriveEntry Cards(string whenConnected = "Act") => new()
    {
        Name = "Camera cards",
        Kind = "CameraCards",
        WhenConnected = whenConnected,
        Actions = [new DriveAction { Action = "Import", From = "DCIM", Extensions = ["jpg", "cr3"], Destination = Pictures + @"\{year}\{month}" }],
    };

    private static DriveEntry Backup(string whenConnected = "Ask") => new()
    {
        Name = "Backup drive",
        Id = Id,
        VolumeSerial = DriveIdentity.HashVolumeSerial(Serial),
        HardwareSerial = DriveIdentity.HashHardwareSerial(HardwareSerial),
        WhenConnected = whenConnected,
        Actions = [new DriveAction { Action = "BackUp", Folders = [Documents], Destination = "Backup" }],
    };

    [Fact]
    public void ACameraCardsPhotosAreImportedIntoMonthFoldersAtOnce()
    {
        Apply(Cards());
        AddCard('F');

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.True(_system.Exists(Pictures + @"\2026\10\IMG_0001.JPG"));
        Assert.True(_system.Exists(Pictures + @"\2026\10\IMG_0002.CR3"));
        Assert.False(_system.Exists(Pictures + @"\2026\10\clip.mov"));
        Assert.Equal(["Camera cards", "Camera cards"], _host.Notifications.Select(notification => notification.Title));
        Assert.Contains("2", _host.Notifications[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACardsPhotosAreImportedOnce()
    {
        Apply(Cards());
        AddCard('F');
        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();
        var copies = _system.Copies.Count;

        _runner.Removed('F');
        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Equal(copies, _system.Copies.Count);
        Assert.False(_system.Exists(Pictures + @"\2026\10\IMG_0001 (2).JPG"));
    }

    [Fact]
    public void ATakenNameGetsANumber()
    {
        Apply(Cards());
        AddCard('F');
        _system.AddFile(Pictures + @"\2026\10\IMG_0001.JPG", size: 5);

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.True(_system.Exists(Pictures + @"\2026\10\IMG_0001 (2).JPG"));
        Assert.Equal(5, _system.FileAt(Pictures + @"\2026\10\IMG_0001.JPG")!.Size);
    }

    [Fact]
    public void ADriveSetToAskGetsTheQuestionAndRunsWhenItsSelected()
    {
        Apply(Backup());
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt");
        _system.AddFile(Documents + @"\Work\plan.docx");

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        var question = Assert.Single(_host.Notifications);
        Assert.Contains("Backup drive", question.Title, StringComparison.Ordinal);
        Assert.Empty(_system.Copies);

        question.Selected!();
        _host.RunAll();

        Assert.True(_system.Exists(@"E:\Backup\Documents\notes.txt"));
        Assert.True(_system.Exists(@"E:\Backup\Documents\Work\plan.docx"));
        Assert.DoesNotContain(_system.FilesIn(@"E:\Backup"), path => path.EndsWith(DriveFiles.TemporarySuffix, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AChangedFileReplacesItsCopyAndAnUnchangedOneIsntCopiedAgain()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt");
        _system.AddFile(Documents + @"\same.txt");
        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();
        var copies = _system.Copies.Count;

        _system.AddFile(Documents + @"\notes.txt", size: 2000, modified: FakeDriveActionsSystem.Taken.AddDays(1));
        _runner.Removed('E');
        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Equal(copies + 1, _system.Copies.Count);
        Assert.Equal(2000, _system.FileAt(@"E:\Backup\Documents\notes.txt")!.Size);
    }

    [Fact]
    public void ACopyChangedOnTheDriveStays()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt");
        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        _system.AddFile(@"E:\Backup\Documents\notes.txt", size: 3000, modified: FakeDriveActionsSystem.Taken.AddDays(2));
        _system.AddFile(Documents + @"\notes.txt", size: 2000, modified: FakeDriveActionsSystem.Taken.AddDays(1));
        _runner.Removed('E');
        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Equal(3000, _system.FileAt(@"E:\Backup\Documents\notes.txt")!.Size);
        Assert.Contains("1", _host.Notifications[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALookAlikeGetsNothingAndAWarning()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E', serial: 0x12345678);
        _system.AddFile(Documents + @"\notes.txt");

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Empty(_system.Copies);
        var warning = Assert.Single(_host.Notifications);
        Assert.Null(warning.Selected);
        Assert.Contains("Backup drive", warning.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ADriveWithTheSameNameAndNoIdFileGetsNothing()
    {
        Apply(Backup("Act"));
        _system.AddDrive('E', label: "Backup drive", serial: Serial, hardwareSerial: HardwareSerial);

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Empty(_system.Copies);
        Assert.Empty(_host.Notifications);
    }

    [Fact]
    public void ADriveConnectedBeforeIsAskedAboutAlsoWhenSetToActAtOnce()
    {
        Apply(Cards());
        AddCard('F');

        _runner.LookAtConnectedDrives();
        _host.RunAll();

        Assert.Empty(_system.Copies);
        Assert.NotNull(Assert.Single(_host.Notifications).Selected);
    }

    [Fact]
    public void WhileThePCIsLockedNothingRunsAndTheDriveIsAskedAboutAfterwards()
    {
        Apply(Cards());
        AddCard('F');

        _runner.SessionLocked();
        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Empty(_system.Copies);
        Assert.Empty(_host.Notifications);

        _runner.SessionUnlocked();
        _host.RunAll();

        Assert.Empty(_system.Copies);
        Assert.NotNull(Assert.Single(_host.Notifications).Selected);
    }

    [Fact]
    public void ADriveSetToDoNothingRunsOnlyWhenAskedTo()
    {
        Apply(Backup("Nothing"));
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt");

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Empty(_host.Notifications);
        var connected = Assert.Single(_host.State.Connected);
        Assert.Equal(('E', "Backup drive"), (connected.Letter, connected.Name));

        _runner.RunRequested('E', _runner.KeyOf('E')!);
        _host.RunAll();

        Assert.True(_system.Exists(@"E:\Backup\Documents\notes.txt"));
    }

    [Fact]
    public void AQuestionForADriveSwappedMeanwhileRunsNothing()
    {
        Apply(Backup());
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\notes.txt");
        _runner.Arrived('E', connectedBefore: false);
        var question = Assert.Single(_host.Notifications);

        _system.RemoveDrive('E');
        AddBackupDrive('E', serial: 0x12345678);
        question.Selected!();
        _host.RunAll();

        Assert.Empty(_system.Copies);
    }

    [Fact]
    public void ADriveAddedWhileItsConnectedIsAskedAbout()
    {
        Apply();
        AddBackupDrive('E');
        _runner.Arrived('E', connectedBefore: false);
        Assert.Empty(_host.Notifications);

        Apply(Backup("Act"));

        Assert.NotNull(Assert.Single(_host.Notifications).Selected);
        Assert.Empty(_system.Copies);
    }

    [Fact]
    public void ABackupThatDoesntFitCopiesNothing()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E');
        _system.SetFree(@"E:\", 500);
        _system.AddFile(Documents + @"\notes.txt");

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Empty(_system.Copies);
        Assert.Contains(_host.Notifications, notification => notification.Text.Contains("1000", StringComparison.Ordinal) || notification.Text.Contains("KB", StringComparison.Ordinal) || notification.Text.Contains("bytes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ADrivePulledOutStopsItsRun()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\a.txt");
        _system.AddFile(Documents + @"\b.txt");
        _system.AddFile(Documents + @"\c.txt");
        _system.BeforeCopy = (_, _) =>
        {
            if (_system.Copies.Count == 1)
            {
                _runner.Removed('E');
            }
        };

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.Single(_system.Copies);
        Assert.Null(_host.State.Running);
    }

    [Fact]
    public void StopEndsTheRunAfterTheFileItsCopying()
    {
        Apply(Cards());
        AddCard('F');
        _runner.Arrived('F', connectedBefore: false);
        Assert.True(_host.RunOne());

        _runner.StopRun();
        _host.RunAll();

        Assert.True(_system.Copies.Count < 2);
        Assert.Null(_host.State.Running);
    }

    [Fact]
    public void RecognizedDrivesAreWatchedUntilTheyGo()
    {
        Apply(Backup());
        AddBackupDrive('E');
        AddCard('F');
        _system.AddDrive('G', label: "OTHER", serial: 1);

        _runner.Arrived('E', connectedBefore: false);
        _runner.Arrived('F', connectedBefore: false);
        _runner.Arrived('G', connectedBefore: false);

        Assert.Equal(['E'], _host.Watched.Order());

        _runner.Removed('E');

        Assert.Empty(_host.Watched);
    }

    [Fact]
    public void OpenAFolderOpensItAfterTheActionsBeforeIt()
    {
        var cards = Cards() with { Actions = [.. Cards().Actions, new DriveAction { Action = "OpenFolder", Folder = "DCIM" }] };
        Apply(cards);
        AddCard('F');

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Equal([@"F:\DCIM"], _host.Opened);
    }

    [Fact]
    public void ARunGoesIntoTheHistory()
    {
        Apply(Cards());
        AddCard('F');

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        var history = new DriveActionsHistoryStore(Path.Join(_data.FullPath, DriveActionsHistoryStore.FileName)).Load(out var readable);
        Assert.True(readable);
        var run = Assert.Single(history.Runs);
        Assert.Equal(("Camera cards", RunOutcome.Done), (run.Drive, run.Outcome));
        var import = Assert.Single(run.Actions);
        Assert.Equal(2, import.Copied);
        Assert.All(import.Files, file => Assert.StartsWith(Pictures, file.NewPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AFileWithAMarkOfTheWebIsntBackedUpToAnExFatDrive()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E', fileSystem: "exFAT");
        _system.AddFile(Documents + @"\setup.pdf", marked: true);
        _system.AddFile(Documents + @"\notes.txt");

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.True(_system.Exists(@"E:\Backup\Documents\notes.txt"));
        Assert.False(_system.Exists(@"E:\Backup\Documents\setup.pdf"));
    }

    [Fact]
    public void AnUnfinishedCopyFromBeforeIsDeleted()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E');
        _system.AddFile(@"E:\Backup\Documents\notes.txt" + DriveFiles.TemporarySuffix, size: 400);
        _system.AddFile(Documents + @"\notes.txt");

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        Assert.False(_system.Exists(@"E:\Backup\Documents\notes.txt" + DriveFiles.TemporarySuffix));
        Assert.Equal(1000, _system.FileAt(@"E:\Backup\Documents\notes.txt")!.Size);
    }

    [Fact]
    public void EjectWhenDoneEjectsTheDriveAfterItsActions()
    {
        Apply(Cards() with { EjectWhenDone = true });
        AddCard('F');

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Equal(['F'], _system.Ejected);
        Assert.Contains("ejected", _host.Notifications[^1].Text, StringComparison.OrdinalIgnoreCase);
        var history = new DriveActionsHistoryStore(Path.Join(_data.FullPath, DriveActionsHistoryStore.FileName)).Load(out _);
        Assert.True(Assert.Single(history.Runs).Ejected);
    }

    [Fact]
    public void ADriveWindowsDoesntEjectSaysSo()
    {
        Apply(Cards() with { EjectWhenDone = true });
        AddCard('F');
        _system.EjectAnswer = new Platform.Windows.EjectResult(Ejected: false, Vetoed: true, 23);

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Contains("didn't eject", _host.Notifications[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatStoppedDoesntEject()
    {
        Apply(Cards() with { EjectWhenDone = true });
        AddCard('F');
        _runner.Arrived('F', connectedBefore: false);
        Assert.True(_host.RunOne());

        _runner.StopRun();
        _host.RunAll();

        Assert.Empty(_system.Ejected);
    }

    [Fact]
    public void ADriveWithoutEjectWhenDoneStays()
    {
        Apply(Cards());
        AddCard('F');

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Empty(_system.Ejected);
        Assert.Contains("remove the drive", _host.Notifications[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALockedDriveIsWatchedAndHandledOnceItsUnlocked()
    {
        Apply(Cards());
        AddCard('F');
        _system.SetLocked('F', true);

        _runner.Arrived('F', connectedBefore: false);
        _host.RunAll();

        Assert.Contains('F', _host.Watched);
        Assert.Empty(_system.Copies);
        Assert.Empty(_host.Notifications);

        _system.SetLocked('F', false);
        _runner.VolumeChanged('F', Platform.Windows.VolumeChange.EncryptionChanged);
        _host.RunAll();

        Assert.Equal(2, _system.Copies.Count);
    }

    [Fact]
    public void ALockedDriveThatCantBeReadForAMomentStaysWatched()
    {
        Apply(Cards());
        AddCard('F');
        _system.SetLocked('F', true);
        _runner.Arrived('F', connectedBefore: false);

        _system.Unreadable.Add('F');
        _runner.VolumeChanged('F', Platform.Windows.VolumeChange.Mounted);

        Assert.Contains('F', _host.Watched);

        _system.Unreadable.Clear();
        _system.SetLocked('F', false);
        _runner.VolumeChanged('F', Platform.Windows.VolumeChange.EncryptionChanged);
        _host.RunAll();

        Assert.Equal(2, _system.Copies.Count);
    }

    [Fact]
    public void ARunIsntIdleWhileItCopies()
    {
        Apply(Cards());
        AddCard('F');
        var idleWhileCopying = true;
        _system.BeforeCopy = (_, _) => idleWhileCopying &= _runner.Running!.Idle.IsSet;
        _runner.Arrived('F', connectedBefore: false);
        var run = _runner.Running!;

        _host.RunAll();

        Assert.False(idleWhileCopying);
        Assert.True(run.Idle.IsSet);
    }

    [Fact]
    public void AnEjectedDrivesRunStops()
    {
        Apply(Backup("Act"));
        AddBackupDrive('E');
        _system.AddFile(Documents + @"\a.txt");
        _system.AddFile(Documents + @"\b.txt");
        _system.BeforeCopy = (_, _) =>
        {
            if (_system.Copies.Count == 1)
            {
                // As the host's UI thread does when Windows asks to eject the drive.
                _runner.Running!.Stop(RunOutcome.Ejected);
                _runner.Ejecting('E');
            }
        };

        _runner.Arrived('E', connectedBefore: false);
        _host.RunAll();

        var history = new DriveActionsHistoryStore(Path.Join(_data.FullPath, DriveActionsHistoryStore.FileName)).Load(out _);
        Assert.Equal(RunOutcome.Ejected, Assert.Single(history.Runs).Outcome);
        Assert.Contains("removed", _host.Notifications[^1].Text, StringComparison.Ordinal);
    }

    public void Dispose() => _data.Dispose();

    private void Apply(params DriveEntry[] drives) => _runner.Apply(new DriveActionsSettings { Enabled = true, Drives = drives });

    private void AddCard(char letter)
    {
        _system.AddDrive(letter, label: "EOS_DIGITAL", fileSystem: "exFAT", serial: 0x5AA18C1A, hardwareSerial: null, bus: 12);
        _system.AddFile($@"{letter}:\DCIM\100CANON\IMG_0001.JPG");
        _system.AddFile($@"{letter}:\DCIM\101CANON\IMG_0002.CR3");
        _system.AddFile($@"{letter}:\DCIM\100CANON\clip.mov");
    }

    private void AddBackupDrive(char letter, uint serial = Serial, string fileSystem = "NTFS")
    {
        _system.AddDrive(letter, label: "BACKUP", fileSystem: fileSystem, serial: serial, hardwareSerial: HardwareSerial);
        _system.AddFile($@"{letter}:\{DriveIdentity.IdFileName}", size: 32, attributes: FileAttributes.Hidden | FileAttributes.System, text: Id);
    }
}
