using System;
using System.IO;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class DriveRecordsStoreTests : IDisposable
{
    private const string Id = "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f";
    private static readonly DateTimeOffset Taken = new(2026, 10, 30, 14, 5, 0, TimeSpan.FromHours(6));

    private readonly TemporaryFolder _folder = new();

    [Fact]
    public void RecordsReadBackAsTheyWereSaved()
    {
        var store = new DriveRecordsStore(Path.Join(_folder.FullPath, DriveRecordsStore.FolderName));
        var records = new DriveRecords();
        records.AddImported(new ImportedFile("IMG_0001.JPG", 1000, Taken));
        records.SetCopy("Documents", @"Work\plan.docx", new BackupCopy(2000, Taken, 2000, Taken.AddSeconds(1)));

        store.Save(Id, records);
        var loaded = store.Load(Id, out var readable);

        Assert.True(readable);
        Assert.True(loaded.WasImported("img_0001.jpg", 1000, Taken));
        Assert.Equal(new BackupCopy(2000, Taken, 2000, Taken.AddSeconds(1)), loaded.CopyOf("documents", @"WORK\plan.docx"));
    }

    [Fact]
    public void EachDriveHasItsOwnRecords()
    {
        var store = new DriveRecordsStore(_folder.FullPath);
        var cards = new DriveRecords();
        cards.AddImported(new ImportedFile("IMG_0001.JPG", 1000, Taken));

        store.Save(DriveRecordsStore.CameraCardsKey, cards);

        Assert.Empty(store.Load(Id, out _).Imported);
        Assert.Single(store.Load(DriveRecordsStore.CameraCardsKey, out _).Imported);
    }

    [Fact]
    public void WithoutAFileTheRecordsAreEmpty()
    {
        var records = new DriveRecordsStore(Path.Join(_folder.FullPath, "missing")).Load(Id, out var readable);

        Assert.True(readable);
        Assert.Empty(records.Imported);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "imported": 5 }""")]
    public void AFileThatCantBeReadGivesEmptyRecords(string text)
    {
        System.IO.File.WriteAllText(Path.Join(_folder.FullPath, Id + ".json"), text);

        var records = new DriveRecordsStore(_folder.FullPath).Load(Id, out var readable);

        Assert.False(readable);
        Assert.Empty(records.Imported);
    }

    [Fact]
    public void ANullInTheFileIsLeftOut()
    {
        System.IO.File.WriteAllText(Path.Join(_folder.FullPath, Id + ".json"), """
            { "imported": [ null, { "name": "IMG_0001.JPG", "size": 1000, "modified": "2026-10-30T14:05:00+06:00" } ], "copies": { "Documents": { "notes.txt": null } } }
            """);

        var records = new DriveRecordsStore(_folder.FullPath).Load(Id, out var readable);

        Assert.True(readable);
        Assert.Single(records.Imported);
        Assert.Null(records.CopyOf("Documents", "notes.txt"));
    }

    [Fact]
    public void RemovedDrivesRecordsAreDeletedAndOtherFilesStay()
    {
        const string removed = "0123456789abcdef0123456789abcdef";
        var store = new DriveRecordsStore(_folder.FullPath);
        store.Save(Id, new DriveRecords());
        store.Save(removed, new DriveRecords());
        store.Save(DriveRecordsStore.CameraCardsKey, new DriveRecords());
        System.IO.File.WriteAllText(Path.Join(_folder.FullPath, "notes.json"), "{}");

        store.DeleteOthers([Id]);

        Assert.True(System.IO.File.Exists(Path.Join(_folder.FullPath, Id + ".json")));
        Assert.False(System.IO.File.Exists(Path.Join(_folder.FullPath, removed + ".json")));
        Assert.False(System.IO.File.Exists(Path.Join(_folder.FullPath, DriveRecordsStore.CameraCardsKey + ".json")));
        Assert.True(System.IO.File.Exists(Path.Join(_folder.FullPath, "notes.json")));
    }

    [Fact]
    public void WithoutTheFolderNothingIsDeleted() => new DriveRecordsStore(Path.Join(_folder.FullPath, "missing")).DeleteOthers([]);

    [Theory]
    [InlineData(@"..\settings")]
    [InlineData("5f0c9a0e")]
    [InlineData("")]
    public void AKeyThatIsntADrivesCantLeaveTheFolder(string key)
    {
        var store = new DriveRecordsStore(_folder.FullPath);

        Assert.Throws<ArgumentException>(() => store.Save(key, new DriveRecords()));
    }

    [Fact]
    public void CameraCardsAndDrivesHaveTheirKeys()
    {
        Assert.Equal(DriveRecordsStore.CameraCardsKey, DriveRecordsStore.KeyOf(new DriveEntry { Kind = "CameraCards" }));
        Assert.Equal(Id, DriveRecordsStore.KeyOf(new DriveEntry { Id = Id }));
    }

    public void Dispose() => _folder.Dispose();
}
