using System.Linq;
using PCCompanion.Modules.Files.DriveActions;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveActionsCheckTests
{
    private static readonly SpecialFolders Places = new(
        @"C:\Windows",
        [@"C:\Program Files", @"C:\Program Files (x86)"],
        @"C:\Users\Ana\AppData\Local\PCCompanion",
        [@"C:\Users\Ana\AppData", @"C:\ProgramData"]);

    private static readonly DriveAction Import = new()
    {
        Action = "Import",
        From = "DCIM",
        Extensions = ["jpg", "cr3"],
        Destination = @"C:\Users\Ana\Pictures\{year}\{month}",
    };

    private static readonly DriveAction BackUp = new()
    {
        Action = "BackUp",
        Folders = [@"C:\Users\Ana\Documents", @"C:\Users\Ana\Pictures"],
        Destination = "Backup",
    };

    private static readonly DriveEntry Stick = new()
    {
        Name = "Backup drive",
        Id = "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f",
        VolumeSerial = "3f9a1c0e7b2d4a6c",
        HardwareSerial = "a1b2c3d4e5f60718",
        Actions = [BackUp],
    };

    private static readonly DriveEntry Cards = new() { Name = "Camera cards", Kind = "CameraCards", Actions = [Import] };

    [Fact]
    public void TheSpecsDrivesCanBeUsed()
    {
        var reviews = DriveActionsCheck.Review(new DriveActionsSettings { Drives = [Stick, Cards] }, Places);

        Assert.All(reviews, review => Assert.Equal(DriveProblem.None, review.Problem));
        Assert.All(reviews.SelectMany(review => review.Actions), problem => Assert.Equal(ActionProblem.None, problem));
    }

    [Fact]
    public void ADriveWithoutAHardwareSerialCanBeUsed()
    {
        Assert.Equal(DriveProblem.None, DriveActionsCheck.CheckDrive(Stick with { HardwareSerial = string.Empty }));
    }

    [Theory]
    [InlineData("", DriveProblem.NoName)]
    [InlineData("   ", DriveProblem.NoName)]
    [InlineData("A name that is much longer than the sixty characters a name can have", DriveProblem.NameTooLong)]
    public void ADriveNeedsAShortName(string name, DriveProblem problem)
    {
        Assert.Equal(problem, DriveActionsCheck.CheckDrive(Stick with { Name = name }));
    }

    [Fact]
    public void ChoicesThisVersionDoesntKnowKeepADriveFromBeingUsed()
    {
        Assert.Equal(DriveProblem.UnknownKind, DriveActionsCheck.CheckDrive(Stick with { Kind = "Phone" }));
        Assert.Equal(DriveProblem.UnknownWhenConnected, DriveActionsCheck.CheckDrive(Stick with { WhenConnected = "Later" }));
    }

    [Theory]
    [InlineData("", "3f9a1c0e7b2d4a6c", "")]
    [InlineData("5f0c9a0e3b1d4c7e", "3f9a1c0e7b2d4a6c", "")]
    [InlineData("5F0C9A0E3B1D4C7E9A2F6B8D1C3E5A7F", "3f9a1c0e7b2d4a6c", "")]
    [InlineData("5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f", "", "")]
    [InlineData("5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f", "3f9a1c0e", "")]
    [InlineData("5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f", "3f9a1c0e7b2d4a6c", "1234")]
    [InlineData("5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7g", "3f9a1c0e7b2d4a6c", "")]
    public void ADriveWithoutAWellFormedIdAndSerialsCouldNeverBeRecognized(string id, string volumeSerial, string hardwareSerial)
    {
        var drive = Stick with { Id = id, VolumeSerial = volumeSerial, HardwareSerial = hardwareSerial };

        Assert.Equal(DriveProblem.NotAdded, DriveActionsCheck.CheckDrive(drive));
    }

    [Fact]
    public void CameraCardsNeedNoIdOrSerials()
    {
        Assert.Equal(DriveProblem.None, DriveActionsCheck.CheckDrive(Cards));
    }

    [Fact]
    public void TwoDrivesCantHaveTheSameName()
    {
        var reviews = DriveActionsCheck.Review(new DriveActionsSettings { Drives = [Stick, Stick with { Name = " backup DRIVE " }] }, Places);

        Assert.Equal([DriveProblem.None, DriveProblem.SameName], reviews.Select(review => review.Problem));
    }

    [Fact]
    public void CameraCardsAreInTheListOnce()
    {
        var reviews = DriveActionsCheck.Review(new DriveActionsSettings { Drives = [Cards, Cards with { Name = "More cards" }] }, Places);

        Assert.Equal([DriveProblem.None, DriveProblem.SecondCameraCards], reviews.Select(review => review.Problem));
    }

    [Fact]
    public void DrivesAndActionsBeyondTheLimitsAreLeftOut()
    {
        var actions = Enumerable.Repeat(Import, DriveActionsSettings.MaximumActions + 1).ToArray();
        var drives = Enumerable.Range(0, DriveActionsSettings.MaximumDrives + 1).Select(number => Stick with { Name = $"Drive {number}", Actions = actions }).ToArray();

        var reviews = DriveActionsCheck.Review(new DriveActionsSettings { Drives = drives }, Places);

        Assert.Equal(DriveProblem.None, reviews[^2].Problem);
        Assert.Equal(ActionProblem.TooMany, reviews[^2].Actions[^1]);
        Assert.Equal(ActionProblem.None, reviews[^2].Actions[^2]);
        Assert.Equal(DriveProblem.TooMany, reviews[^1].Problem);
        Assert.All(reviews[^1].Actions, problem => Assert.Equal(ActionProblem.TooMany, problem));
    }

    [Theory]
    [InlineData("DCIM", true)]
    [InlineData(@"DCIM\100CANON", true)]
    [InlineData(@"Backup\Ana's laptop", true)]
    [InlineData("", true)]
    [InlineData(@"\DCIM", false)]
    [InlineData(@"DCIM\", false)]
    [InlineData(@"E:\DCIM", false)]
    [InlineData("E:DCIM", false)]
    [InlineData("DCIM/100CANON", false)]
    [InlineData(@"DCIM\..\Windows", false)]
    [InlineData(@".\DCIM", false)]
    [InlineData("DC|IM", false)]
    [InlineData("DCIM*", false)]
    [InlineData("CON", false)]
    [InlineData("Backup.", false)]
    public void AFolderOnTheDriveHasNoLetterAndStaysOnTheDrive(string folder, bool valid)
    {
        Assert.Equal(valid, DriveActionsCheck.IsDriveFolder(folder, allowRoot: true));
    }

    [Fact]
    public void ABackupCantGoToTheDrivesRoot()
    {
        Assert.Equal(ActionProblem.InvalidDriveFolder, DriveActionsCheck.CheckAction(BackUp with { Destination = string.Empty }, DriveEntryKind.Drive, Places));
    }

    [Fact]
    public void AnImportCanStartAtTheDrivesRoot()
    {
        Assert.Equal(ActionProblem.None, DriveActionsCheck.CheckAction(Import with { From = string.Empty }, DriveEntryKind.Drive, Places));
    }

    [Fact]
    public void AnImportNeedsTypes()
    {
        Assert.Equal(ActionProblem.NoTypes, DriveActionsCheck.CheckAction(Import with { Extensions = [] }, DriveEntryKind.CameraCards, Places));
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData("j*")]
    [InlineData(" jpg")]
    [InlineData("")]
    [InlineData(@"jp\g")]
    public void AnImportsTypesAreExtensionsWithoutTheDot(string extension)
    {
        Assert.Equal(ActionProblem.InvalidType, DriveActionsCheck.CheckAction(Import with { Extensions = ["cr3", extension] }, DriveEntryKind.CameraCards, Places));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"Pictures\{year}")]
    [InlineData(@"C:\Windows\{year}")]
    [InlineData(@"C:\Users\Ana\AppData\Local\PCCompanion")]
    [InlineData(@"C:\Users\Ana\Pictures\{yaer}")]
    public void AnImportsDestinationIsCheckedAsFolderRulesChecksIt(string destination)
    {
        Assert.Equal(ActionProblem.DestinationNotAllowed, DriveActionsCheck.CheckAction(Import with { Destination = destination }, DriveEntryKind.CameraCards, Places));
    }

    [Fact]
    public void TheCameraFilesPresetMakesAnImportOfPhotosRawPhotosAndVideos()
    {
        var extensions = FileKinds.Extensions(FileKind.CameraFiles);

        Assert.Equal(ActionProblem.None, DriveActionsCheck.CheckAction(Import with { Extensions = extensions }, DriveEntryKind.CameraCards, Places));
        Assert.Equal(extensions.Count, extensions.Distinct().Count());
        Assert.Contains("cr3", extensions);
        Assert.Contains("mts", extensions);
    }

    [Fact]
    public void AnImportsChoiceForATakenNameMustBeKnown()
    {
        Assert.Equal(ActionProblem.UnknownIfExists, DriveActionsCheck.CheckAction(Import with { IfExists = "Replace" }, DriveEntryKind.CameraCards, Places));
    }

    [Fact]
    public void CameraCardsGetNoBackups()
    {
        Assert.Equal(ActionProblem.NotForCameraCards, DriveActionsCheck.CheckAction(BackUp, DriveEntryKind.CameraCards, Places));
    }

    [Fact]
    public void ABackupNeedsFoldersWithinTheLimit()
    {
        Assert.Equal(ActionProblem.NoFolders, DriveActionsCheck.CheckAction(BackUp with { Folders = [] }, DriveEntryKind.Drive, Places));

        var folders = Enumerable.Range(0, DriveActionsSettings.MaximumFolders + 1).Select(number => $@"C:\Users\Ana\Folder {number}").ToArray();
        Assert.Equal(ActionProblem.TooManyFolders, DriveActionsCheck.CheckAction(BackUp with { Folders = folders }, DriveEntryKind.Drive, Places));
    }

    [Theory]
    [InlineData(@"D:\")]
    [InlineData(@"C:\Windows\Temp")]
    [InlineData(@"C:\Program Files\App")]
    [InlineData(@"C:\Users\Ana\AppData\Local\PCCompanion")]
    [InlineData("Documents")]
    public void ABackupsFoldersAreCheckedAsFolderRulesChecksThem(string folder)
    {
        Assert.Equal(ActionProblem.FolderNotAllowed, DriveActionsCheck.CheckAction(BackUp with { Folders = [folder] }, DriveEntryKind.Drive, Places));
    }

    [Fact]
    public void TwoOfABackupsFoldersCantHaveTheSameName()
    {
        var backUp = BackUp with { Folders = [@"C:\Users\Ana\Documents", @"D:\Work\documents"] };

        Assert.Equal(ActionProblem.SameFolderName, DriveActionsCheck.CheckAction(backUp, DriveEntryKind.Drive, Places));
    }

    [Theory]
    [InlineData("DCIM", ActionProblem.None)]
    [InlineData("", ActionProblem.None)]
    [InlineData(@"C:\Users\Ana\Pictures", ActionProblem.None)]
    [InlineData(@"\\server\share\Photos", ActionProblem.None)]
    [InlineData(@"..\DCIM", ActionProblem.InvalidFolder)]
    [InlineData("C:Pictures", ActionProblem.InvalidFolder)]
    public void AFolderToOpenIsOnTheDriveOrOnThePC(string folder, ActionProblem problem)
    {
        Assert.Equal(problem, DriveActionsCheck.CheckAction(new DriveAction { Action = "OpenFolder", Folder = folder }, DriveEntryKind.CameraCards, Places));
    }

    [Fact]
    public void AnActionThisVersionDoesntKnowIsntUsed()
    {
        Assert.Equal(ActionProblem.UnknownAction, DriveActionsCheck.CheckAction(Import with { Action = "Sync" }, DriveEntryKind.Drive, Places));
    }
}
