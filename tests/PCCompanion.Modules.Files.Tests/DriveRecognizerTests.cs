using System.Collections.Generic;
using PCCompanion.Modules.Files.DriveActions;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveRecognizerTests
{
    private const string Id = "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f";
    private const uint VolumeSerial = 0xA8D4F2F8;
    private const string HardwareSerial = "0LVF0HQ5";

    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", []);

    private static readonly ConnectedDrive Stick = new(
        Volume: @"\\?\Volume{bbe4fa19-0000-0000-0000-010000000000}\",
        Letter: 'E',
        Kind: ConnectedDriveKind.UsbDrive,
        IsLocked: false,
        Label: "BACKUP",
        FileSystem: "exFAT",
        VolumeSerial: VolumeSerial,
        HardwareSerial: HardwareSerial,
        Size: 64L << 30,
        Free: 32L << 30,
        KeepsStreams: false,
        IsReadOnly: false,
        HasDcim: false,
        IdFileText: Id + "\r\n");

    private static readonly DriveEntry Backup = new()
    {
        Name = "Backup drive",
        Id = Id,
        VolumeSerial = DriveIdentity.HashVolumeSerial(VolumeSerial),
        HardwareSerial = DriveIdentity.HashHardwareSerial(HardwareSerial),
    };

    private static readonly DriveEntry Cards = new() { Name = "Camera cards", Kind = "CameraCards" };

    [Fact]
    public void ADriveWithItsIdFileAndSerialsIsRecognized()
    {
        Assert.Equal(new Recognition(RecognitionOutcome.Recognized, 1), Recognize(Stick, Cards, Backup));
    }

    [Fact]
    public void ADriveWithoutAHardwareSerialIsRecognizedByTheOthers()
    {
        var drive = Stick with { HardwareSerial = null };
        var entry = Backup with { HardwareSerial = string.Empty };

        Assert.Equal(RecognitionOutcome.Recognized, Recognize(drive, entry).Outcome);
    }

    [Fact]
    public void ALookAlikeWithTheSameNameAndNoIdFileGetsNothing()
    {
        var lookAlike = Stick with { IdFileText = null, VolumeSerial = 0x12345678, HardwareSerial = "OTHER" };

        Assert.Equal(RecognitionOutcome.NotRecognized, Recognize(lookAlike, Backup).Outcome);
    }

    [Theory]
    [InlineData(0x12345678u, HardwareSerial)]
    [InlineData(VolumeSerial, "OTHER")]
    [InlineData(VolumeSerial, null)]
    public void ADriveWithAnotherDrivesIdFileAndOtherSerialsIsALookAlike(uint volumeSerial, string? hardwareSerial)
    {
        var copy = Stick with { VolumeSerial = volumeSerial, HardwareSerial = hardwareSerial };

        Assert.Equal(new Recognition(RecognitionOutcome.LookAlike, 0), Recognize(copy, Backup));
    }

    [Fact]
    public void ALookAlikeWithADcimFolderIsntTakenForACameraCard()
    {
        var copy = Stick with { VolumeSerial = 0x12345678, HasDcim = true };

        Assert.Equal(RecognitionOutcome.LookAlike, Recognize(copy, Backup, Cards).Outcome);
    }

    [Fact]
    public void AReformattedDriveIsntRecognized()
    {
        var reformatted = Stick with { IdFileText = null, VolumeSerial = 0x62E35BFA };

        Assert.Equal(RecognitionOutcome.NotRecognized, Recognize(reformatted, Backup).Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an id")]
    public void AnIdFileWithoutAnIdCountsAsNone(string text)
    {
        Assert.Equal(RecognitionOutcome.NotRecognized, Recognize(Stick with { IdFileText = text }, Backup).Outcome);
    }

    [Fact]
    public void ADriveWithADcimFolderIsACameraCard()
    {
        var card = Stick with { IdFileText = null, HasDcim = true, Kind = ConnectedDriveKind.MemoryCard };

        Assert.Equal(new Recognition(RecognitionOutcome.CameraCards, 1), Recognize(card, Backup, Cards));
    }

    [Fact]
    public void ADriveInTheListIsThatDriveAlsoWithADcimFolder()
    {
        Assert.Equal(new Recognition(RecognitionOutcome.Recognized, 1), Recognize(Stick with { HasDcim = true }, Cards, Backup));
    }

    [Fact]
    public void ADcimFolderMeansNothingWithoutCameraCardsInTheList()
    {
        Assert.Equal(RecognitionOutcome.NotRecognized, Recognize(Stick with { IdFileText = null, HasDcim = true }, Backup).Outcome);
    }

    [Fact]
    public void DrivesWithAProblemAreLeftOut()
    {
        var unknownKind = Backup with { WhenConnected = "Later" };

        Assert.Equal(RecognitionOutcome.NotRecognized, Recognize(Stick, unknownKind).Outcome);
        Assert.Equal(RecognitionOutcome.NotRecognized, Recognize(Stick with { IdFileText = null, HasDcim = true }, Cards with { Name = string.Empty }).Outcome);
    }

    [Fact]
    public void ALockedDriveCantBeRecognizedYet()
    {
        var locked = Stick with { IsLocked = true, IdFileText = null, VolumeSerial = 0 };

        Assert.Equal(RecognitionOutcome.Locked, Recognize(locked, Backup).Outcome);
    }

    [Fact]
    public void ADriveThatDoesntCountIsntRecognized()
    {
        Assert.Equal(RecognitionOutcome.NotCounted, Recognize(Stick with { Kind = null }, Backup).Outcome);
    }

    [Fact]
    public void ADriveIsAddedWithItsIdAndTheHashesOfItsSerials()
    {
        var entry = DriveRecognizer.NewEntry(Stick, Id, "USB drive");

        Assert.Equal("BACKUP", entry.Name);
        Assert.Equal(DriveEntryKind.Drive, entry.KnownKind);
        Assert.Equal(WhenConnectedChoice.Ask, entry.KnownWhenConnected);
        Assert.False(entry.EjectWhenDone);
        Assert.Equal(Backup.Id, entry.Id);
        Assert.Equal(Backup.VolumeSerial, entry.VolumeSerial);
        Assert.Equal(Backup.HardwareSerial, entry.HardwareSerial);
        Assert.Equal(DriveProblem.None, DriveActionsCheck.CheckDrive(entry));
    }

    [Fact]
    public void ADriveWithoutALabelIsNamedAfterItsKind()
    {
        Assert.Equal("USB drive", DriveRecognizer.NewEntry(Stick with { Label = " " }, Id, "USB drive").Name);
    }

    [Fact]
    public void ALongLabelIsCutToTheLongestName()
    {
        var entry = DriveRecognizer.NewEntry(Stick with { Label = new string('A', 80) }, Id, "USB drive");

        Assert.Equal(DriveActionsSettings.MaximumNameLength, entry.Name.Length);
    }

    [Fact]
    public void UseThisDriveRecordsTheSerialsOfTheDriveWithTheIdFile()
    {
        var copy = Stick with { VolumeSerial = 0x12345678, HardwareSerial = "OTHER" };

        var entry = DriveRecognizer.WithSerialsOf(Backup, copy);

        Assert.Equal(RecognitionOutcome.Recognized, Recognize(copy, entry).Outcome);
        Assert.Equal(RecognitionOutcome.LookAlike, Recognize(Stick, entry).Outcome);
    }

    [Theory]
    [MemberData(nameof(AddCases))]
    public void ADriveCanBeAddedWhenItCountsAndIsntInTheList(ConnectedDrive drive, AddProblem problem)
    {
        var drives = new[] { Backup };

        Assert.Equal(problem, DriveRecognizer.CheckAdd(drive, drives, DriveActionsCheck.Review(new DriveActionsSettings { Drives = drives }, Places)));
    }

    public static TheoryData<ConnectedDrive, AddProblem> AddCases() => new()
    {
        { Stick with { IdFileText = null, VolumeSerial = 0x12345678 }, AddProblem.None },
        { Stick with { Kind = null }, AddProblem.NotCounted },
        { Stick with { IsLocked = true }, AddProblem.Locked },
        { Stick with { IdFileText = null, IsReadOnly = true }, AddProblem.ReadOnly },
        { Stick, AddProblem.AlreadyAdded },
        { Stick with { VolumeSerial = 0x12345678 }, AddProblem.LookAlike },
    };

    private static Recognition Recognize(ConnectedDrive drive, params DriveEntry[] drives)
    {
        IReadOnlyList<DriveEntry> list = drives;
        return DriveRecognizer.Recognize(drive, list, DriveActionsCheck.Review(new DriveActionsSettings { Drives = list }, Places));
    }
}
