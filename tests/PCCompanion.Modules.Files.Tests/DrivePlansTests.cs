using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DrivePlansTests
{
    private const string Pictures = @"C:\Users\Ana\Pictures";
    private const string Documents = @"C:\Users\Ana\Documents";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Taken = new(2026, 10, 30, 14, 5, 0, TimeSpan.Zero);
    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", []);

    private static readonly DriveAction Import = new()
    {
        Action = "Import",
        From = "DCIM",
        Extensions = ["jpg", "cr3"],
        Destination = Pictures + @"\{year}\{month}",
    };

    private static readonly ConnectedDrive Stick = new(@"\\?\Volume{1}\", 'E', ConnectedDriveKind.UsbDrive, false, "BACKUP", "NTFS", 1, "SERIAL", 64L << 30, 32L << 30, true, false, false, null);

    [Fact]
    public void AnImportCopiesTheFilesOfItsTypesIntoFoldersMadeFromThem()
    {
        var plan = ImportPlanner.Plan(Import, @"E:\DCIM", [Photo(@"100CANON\IMG_0001.JPG"), Photo(@"101CANON\IMG_0002.CR3"), Photo(@"100CANON\clip.mov")], new DriveRecords(), Zone, Places);

        Assert.Equal([@"100CANON\IMG_0001.JPG", @"101CANON\IMG_0002.CR3"], plan.Files.Select(file => file.RelativePath));
        Assert.All(plan.Files, file => Assert.Equal(Pictures + @"\2026\10", file.Destination));
        Assert.Equal(@"E:\DCIM\100CANON\IMG_0001.JPG", plan.Files[0].Source);
        Assert.Equal(2 * 1000, plan.Bytes);
    }

    [Fact]
    public void AnImportLeavesHiddenEmptyAndOnlineOnlyFilesLinksAndUnfinishedCopies()
    {
        var files = new[]
        {
            Photo("hidden.jpg", attributes: FileAttributes.Hidden),
            Photo("system.jpg", attributes: FileAttributes.System),
            Photo("empty.jpg", size: 0),
            Photo("online.jpg", attributes: FileAttributes.Offline),
            Photo("link.jpg", attributes: FileAttributes.ReparsePoint, reparseTag: 0xA000000C),
            Photo("photo.jpg" + DriveFiles.TemporarySuffix),
            Photo("AUX.jpg"),
            Photo("kept.jpg"),
        };

        var plan = ImportPlanner.Plan(Import, @"E:\DCIM", files, new DriveRecords(), Zone, Places);

        Assert.Equal(["kept.jpg"], plan.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void FilesImportedBeforeAreListedButNotCopiedUnlessChosen()
    {
        var records = new DriveRecords();
        records.AddImported(new ImportedFile("IMG_0001.JPG", 1000, Taken));

        var plan = ImportPlanner.Plan(Import, @"E:\DCIM", [Photo("IMG_0001.JPG"), Photo("IMG_0002.JPG")], records, Zone, Places);

        Assert.Equal([true, false], plan.Files.Select(file => file.ImportedBefore));
        Assert.Equal(["IMG_0002.JPG"], plan.New.Select(file => file.RelativePath));
        Assert.Equal(1000, plan.Bytes);
    }

    [Theory]
    [InlineData("img_0001.jpg", 1000, 0, true)]
    [InlineData("IMG_0001.JPG", 1000, 2, true)]
    [InlineData("IMG_0001.JPG", 1000, 3600, true)]
    [InlineData("IMG_0001.JPG", 1000, -3600, true)]
    [InlineData("IMG_0001.JPG", 1000, 7200, false)]
    [InlineData("IMG_0001.JPG", 1001, 0, false)]
    [InlineData("IMG_0002.JPG", 1000, 0, false)]
    public void AFileCountsAsImportedBeforeByItsNameSizeAndATimeWithinAnHour(string name, long size, int seconds, bool imported)
    {
        var records = new DriveRecords();
        records.AddImported(new ImportedFile("IMG_0001.JPG", 1000, Taken));

        Assert.Equal(imported, records.WasImported(name, size, Taken.AddSeconds(seconds)));
    }

    [Fact]
    public void TheOldestImportedFilesAreForgottenBeyondTheLimit()
    {
        var records = new DriveRecords();
        for (var number = 0; number <= DriveRecords.MaximumImportedFiles; number++)
        {
            records.AddImported(new ImportedFile($"IMG_{number}.JPG", 1000, Taken));
        }

        Assert.Equal(DriveRecords.MaximumImportedFiles, records.Imported.Count);
        Assert.False(records.WasImported("IMG_0.JPG", 1000, Taken));
        Assert.True(records.WasImported("IMG_1.JPG", 1000, Taken));
    }

    [Fact]
    public void ABackupCopiesFilesTheDriveHasNoCopyOf()
    {
        var plan = Plan([File("notes.txt"), File(@"Work\plan.docx")], []);

        Assert.Equal([BackupStep.Copy, BackupStep.Copy], plan.Files.Select(file => file.Step));
        Assert.Equal(2 * 1000, plan.Bytes);
        Assert.Equal(Documents + @"\notes.txt", plan.Files[0].Source);
        Assert.Equal("Documents", plan.Files[0].Folder);
    }

    [Fact]
    public void ABackupLeavesUpToDateCopiesAlone()
    {
        var records = Recorded("notes.txt", File("notes.txt").Details);

        var plan = Plan([File("notes.txt")], [File("notes.txt")], records);

        Assert.Equal(BackupStep.UpToDate, Assert.Single(plan.Files).Step);
        Assert.Equal(0, plan.Bytes);
    }

    [Fact]
    public void AChangedFileReplacesItsUnchangedCopy()
    {
        var records = Recorded("notes.txt", File("notes.txt").Details);

        var plan = Plan([File("notes.txt", size: 2000, modified: Taken.AddDays(1))], [File("notes.txt")], records);

        Assert.Equal(BackupStep.Replace, Assert.Single(plan.Files).Step);
        Assert.Equal(2000, plan.Bytes);
    }

    [Fact]
    public void ACopyOnAFatDriveIsUnchangedWithItsTimeInStepsOf2SecondsAndAnHourApart()
    {
        var records = Recorded("notes.txt", File("notes.txt").Details);

        Assert.Equal(BackupStep.UpToDate, Plan([File("notes.txt")], [File("notes.txt", modified: Taken.AddSeconds(1))], records).Files[0].Step);
        Assert.Equal(BackupStep.UpToDate, Plan([File("notes.txt")], [File("notes.txt", modified: Taken.AddHours(1))], records).Files[0].Step);
    }

    [Fact]
    public void ACopyChangedOnTheDriveStays()
    {
        var records = Recorded("notes.txt", File("notes.txt").Details);

        var plan = Plan([File("notes.txt", modified: Taken.AddDays(1))], [File("notes.txt", size: 3000, modified: Taken.AddDays(2))], records);

        Assert.Equal((BackupStep.Skip, BackupSkip.ChangedOnDrive), (plan.Files[0].Step, plan.Files[0].Reason));
    }

    [Fact]
    public void AFilePCCompanionDidntPutThereStays()
    {
        var plan = Plan([File("notes.txt")], [File("notes.txt", size: 3000)]);

        Assert.Equal((BackupStep.Skip, BackupSkip.NotOurs), (plan.Files[0].Step, plan.Files[0].Reason));
    }

    [Fact]
    public void AFolderWithTheFilesNameStays()
    {
        var plan = Plan([File("notes.txt")], [File("notes.txt", attributes: FileAttributes.Directory)]);

        Assert.Equal((BackupStep.Skip, BackupSkip.NotOurs), (plan.Files[0].Step, plan.Files[0].Reason));
    }

    [Fact]
    public void ACopyTheSameAsTheFileIsAdoptedAsAfterAReinstall()
    {
        var plan = Plan([File("notes.txt")], [File("notes.txt", modified: Taken.AddSeconds(1))]);

        Assert.Equal(BackupStep.Adopt, Assert.Single(plan.Files).Step);
        Assert.Equal(0, plan.Bytes);
    }

    [Fact]
    public void AReadOnlyCopyIsntReplaced()
    {
        var records = Recorded("notes.txt", File("notes.txt").Details);

        var plan = Plan([File("notes.txt", modified: Taken.AddDays(1))], [File("notes.txt", attributes: FileAttributes.ReadOnly)], records);

        Assert.Equal((BackupStep.Skip, BackupSkip.ReadOnly), (plan.Files[0].Step, plan.Files[0].Reason));
    }

    [Fact]
    public void ACopyDeletedFromTheDriveIsMadeAgain()
    {
        var records = Recorded("notes.txt", File("notes.txt").Details);

        Assert.Equal(BackupStep.Copy, Plan([File("notes.txt")], [], records).Files[0].Step);
    }

    [Fact]
    public void AFileWithAMarkOfTheWebIsntCopiedToADriveThatCantKeepIt()
    {
        var exFat = Stick with { FileSystem = "exFAT", KeepsStreams = false };
        var marked = new HashSet<string> { Documents + @"\setup.pdf" };

        var plan = BackupPlanner.Plan([Source(File("setup.pdf"), File("notes.txt"))], new Dictionary<string, IReadOnlyList<ListedFile>>(), exFat, new DriveRecords(), marked.Contains);

        Assert.Equal([(BackupStep.Copy, BackupSkip.None), (BackupStep.Skip, BackupSkip.MarkWouldBeLost)], plan.Files.Select(file => (file.Step, file.Reason)));
    }

    [Fact]
    public void MarksAreReadOnlyForADriveThatCantKeepThem()
    {
        var asked = 0;

        BackupPlanner.Plan([Source(File("setup.pdf"))], new Dictionary<string, IReadOnlyList<ListedFile>>(), Stick, new DriveRecords(), _ => ++asked > 0);

        Assert.Equal(0, asked);
    }

    [Fact]
    public void AFileOf4GbOrMoreIsntCopiedToAFat32Drive()
    {
        var fat32 = Stick with { FileSystem = "FAT32", KeepsStreams = false };

        var plan = BackupPlanner.Plan([Source(File("video.mp4", size: 4L << 30), File("small.mp4"))], new Dictionary<string, IReadOnlyList<ListedFile>>(), fat32, new DriveRecords(), _ => false);

        Assert.Equal([(BackupStep.Copy, BackupSkip.None), (BackupStep.Skip, BackupSkip.TooLarge)], plan.Files.Select(file => (file.Step, file.Reason)));
    }

    [Fact]
    public void ABackupLeavesHiddenAndOnlineOnlyFilesLinksAndUnfinishedCopies()
    {
        var plan = Plan(
            [File("desktop.ini", attributes: FileAttributes.Hidden | FileAttributes.System), File("cloud.docx", attributes: FileAttributes.Offline), File("link.txt", attributes: FileAttributes.ReparsePoint, reparseTag: 0xA000000C), File("notes.txt" + DriveFiles.TemporarySuffix), File("notes.txt")],
            []);

        Assert.Equal(["notes.txt"], plan.Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void AnUnfinishedCopyOnTheDriveIsntTakenForACopy()
    {
        var plan = Plan([File("notes.txt")], [File("notes.txt" + DriveFiles.TemporarySuffix)]);

        Assert.Equal(BackupStep.Copy, plan.Files[0].Step);
    }

    [Fact]
    public void FileNamesAreComparedIgnoringCase()
    {
        var records = Recorded("NOTES.TXT", File("notes.txt").Details);

        Assert.Equal(BackupStep.UpToDate, Plan([File("notes.txt")], [File("Notes.Txt")], records).Files[0].Step);
    }

    private static ListedFile Photo(string path, long size = 1000, FileAttributes attributes = FileAttributes.Archive, uint reparseTag = 0) =>
        new(path, Details(Path.GetFileName(path), size, Taken, attributes, reparseTag));

    private static ListedFile File(string path, long size = 1000, DateTimeOffset? modified = null, FileAttributes attributes = FileAttributes.Archive, uint reparseTag = 0) =>
        new(path, Details(Path.GetFileName(path), size, modified ?? Taken, attributes, reparseTag));

    private static FileDetails Details(string name, long size, DateTimeOffset modified, FileAttributes attributes, uint reparseTag) =>
        new(name, attributes, reparseTag, size, modified, modified, modified, null);

    private static BackupSource Source(params ListedFile[] files) => new("Documents", Documents, files);

    private static DriveRecords Recorded(string path, FileDetails file)
    {
        var records = new DriveRecords();
        records.SetCopy("documents", path, new BackupCopy(file.Size, file.Modified, file.Size, file.Modified));
        return records;
    }

    private static BackupPlan Plan(IReadOnlyList<ListedFile> files, IReadOnlyList<ListedFile> copies, DriveRecords? records = null) =>
        BackupPlanner.Plan([new BackupSource("Documents", Documents, files)], new Dictionary<string, IReadOnlyList<ListedFile>> { ["Documents"] = copies }, Stick, records ?? new DriveRecords(), _ => false);
}
