using System.Text.Json;
using PCCompanion.Modules.Files.DriveActions;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveActionsSettingsTests
{
    // The spec's example.
    private const string Example = """
        {
          "enabled": true,
          "drives": [
            {
              "name": "Backup drive",
              "kind": "Drive",
              "id": "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f",
              "volumeSerial": "3f9a1c0e7b2d4a6c",
              "hardwareSerial": "a1b2c3d4e5f60718",
              "whenConnected": "Ask",
              "ejectWhenDone": true,
              "actions": [
                {
                  "action": "BackUp",
                  "folders": ["C:\\Users\\Ana\\Documents", "C:\\Users\\Ana\\Pictures"],
                  "destination": "Backup"
                }
              ]
            },
            {
              "name": "Camera cards",
              "kind": "CameraCards",
              "whenConnected": "Act",
              "ejectWhenDone": false,
              "actions": [
                {
                  "action": "Import",
                  "from": "DCIM",
                  "extensions": ["jpg", "heic", "cr3", "mp4"],
                  "destination": "C:\\Users\\Ana\\Pictures\\{year}\\{month}",
                  "ifExists": "AddNumber"
                },
                {
                  "action": "OpenFolder",
                  "folder": "C:\\Users\\Ana\\Pictures"
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoDrives()
    {
        var settings = DriveActionsSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Drives);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = DriveActionsSettings.Read(Section(Example));

        Assert.True(settings.Enabled);
        Assert.Equal(2, settings.Drives.Count);

        var backup = settings.Drives[0];
        Assert.Equal("Backup drive", backup.Name);
        Assert.Equal(DriveEntryKind.Drive, backup.KnownKind);
        Assert.Equal("5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f", backup.Id);
        Assert.Equal("3f9a1c0e7b2d4a6c", backup.VolumeSerial);
        Assert.Equal("a1b2c3d4e5f60718", backup.HardwareSerial);
        Assert.Equal(WhenConnectedChoice.Ask, backup.KnownWhenConnected);
        Assert.True(backup.EjectWhenDone);
        var backUp = Assert.Single(backup.Actions);
        Assert.Equal(DriveActionKind.BackUp, backUp.KnownAction);
        Assert.Equal([@"C:\Users\Ana\Documents", @"C:\Users\Ana\Pictures"], backUp.Folders);
        Assert.Equal("Backup", backUp.Destination);

        var cards = settings.Drives[1];
        Assert.Equal(DriveEntryKind.CameraCards, cards.KnownKind);
        Assert.Equal(WhenConnectedChoice.Act, cards.KnownWhenConnected);
        Assert.False(cards.EjectWhenDone);
        Assert.Equal(2, cards.Actions.Count);
        var import = cards.Actions[0];
        Assert.Equal(DriveActionKind.Import, import.KnownAction);
        Assert.Equal("DCIM", import.From);
        Assert.Equal(["jpg", "heic", "cr3", "mp4"], import.Extensions);
        Assert.Equal(@"C:\Users\Ana\Pictures\{year}\{month}", import.Destination);
        Assert.Equal(NameConflict.AddNumber, import.KnownIfExists);
        Assert.Equal(DriveActionKind.OpenFolder, cards.Actions[1].KnownAction);
        Assert.Equal(@"C:\Users\Ana\Pictures", cards.Actions[1].Folder);
    }

    [Fact]
    public void WrittenSettingsReadBackTheSame()
    {
        var settings = DriveActionsSettings.Read(Section(Example));

        Assert.Equal(settings, DriveActionsSettings.Read(settings.Write()));
    }

    [Fact]
    public void TurningItOffKeepsTheDrives()
    {
        var settings = DriveActionsSettings.Read(Section(Example)).WithEnabled(false);

        Assert.False(settings.Enabled);
        Assert.Equal(2, settings.Drives.Count);
    }

    [Fact]
    public void MissingValuesGetTheirDefaults()
    {
        var settings = DriveActionsSettings.Read(Section("""
            { "drives": [ { "name": "Stick", "actions": [ { } ] } ] }
            """));

        var drive = Assert.Single(settings.Drives);
        Assert.Equal(DriveEntryKind.Drive, drive.KnownKind);
        Assert.Equal(WhenConnectedChoice.Ask, drive.KnownWhenConnected);
        Assert.False(drive.EjectWhenDone);
        Assert.Equal(string.Empty, drive.HardwareSerial);
        var action = Assert.Single(drive.Actions);
        Assert.Equal(DriveActionKind.Import, action.KnownAction);
        Assert.Equal("DCIM", action.From);
        Assert.Equal(NameConflict.AddNumber, action.KnownIfExists);
        Assert.Empty(action.Extensions);
        Assert.Empty(action.Folders);
    }

    [Fact]
    public void ValuesThisVersionDoesntKnowAreKeptWhenSaved()
    {
        var settings = DriveActionsSettings.Read(Section("""
            { "drives": [ { "name": "Phone", "kind": "Phone", "whenConnected": "Later", "actions": [ { "action": "Sync", "ifExists": "Replace" } ] } ] }
            """));

        var drive = settings.Drives[0];
        Assert.Null(drive.KnownKind);
        Assert.Null(drive.KnownWhenConnected);
        Assert.Null(drive.Actions[0].KnownAction);
        Assert.Null(drive.Actions[0].KnownIfExists);

        var saved = DriveActionsSettings.Read(settings.Write()).Drives[0];
        Assert.Equal("Phone", saved.Kind);
        Assert.Equal("Later", saved.WhenConnected);
        Assert.Equal("Sync", saved.Actions[0].Action);
        Assert.Equal("Replace", saved.Actions[0].IfExists);
    }

    [Fact]
    public void ChoicesAreReadIgnoringCase()
    {
        var settings = DriveActionsSettings.Read(Section("""
            { "drives": [ { "name": "Cards", "kind": "cameracards", "whenConnected": "act", "actions": [ { "action": "openfolder", "ifExists": "skip" } ] } ] }
            """));

        var drive = settings.Drives[0];
        Assert.Equal(DriveEntryKind.CameraCards, drive.KnownKind);
        Assert.Equal(WhenConnectedChoice.Act, drive.KnownWhenConnected);
        Assert.Equal(DriveActionKind.OpenFolder, drive.Actions[0].KnownAction);
        Assert.Equal(NameConflict.Skip, drive.Actions[0].KnownIfExists);
    }

    [Fact]
    public void ReadLeavesOutANullInAnyList()
    {
        var settings = DriveActionsSettings.Read(Section("""
            {
              "drives": [
                null,
                { "name": "Stick", "actions": [ null, { "extensions": ["jpg", null], "folders": [null, "C:\\Users\\Ana\\Documents"] } ] }
              ]
            }
            """));

        var drive = Assert.Single(settings.Drives);
        var action = Assert.Single(drive.Actions);
        Assert.Equal(["jpg"], action.Extensions);
        Assert.Equal([@"C:\Users\Ana\Documents"], action.Folders);
    }

    [Theory]
    [InlineData("""{ "drives": 5 }""")]
    [InlineData("""{ "drives": [ { "name": null } ] }""")]
    [InlineData("""{ "drives": [ { "name": "Stick", "ejectWhenDone": "yes" } ] }""")]
    public void ASectionThatDoesntMatchTheFormatCantBeRead(string json)
    {
        Assert.ThrowsAny<JsonException>(() => DriveActionsSettings.Read(Section(json)));
    }
}
