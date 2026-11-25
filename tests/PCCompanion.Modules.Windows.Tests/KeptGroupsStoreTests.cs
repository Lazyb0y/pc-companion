using System;
using System.IO;
using PCCompanion.Modules.Windows.WindowTabs;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

// Each test uses a folder of its own under the temp folder.
public sealed class KeptGroupsStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly KeptGroupsStore _store;

    public KeptGroupsStoreTests()
    {
        _store = new KeptGroupsStore(Path.Combine(_folder.FullPath, KeptGroupsStore.FileName));
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void WithoutAFileThereAreNoGroups()
    {
        Assert.Empty(_store.Load(out var readable));
        Assert.True(readable);
    }

    [Fact]
    public void SavedGroupsAreReadBackInTheirOrder()
    {
        KeptGroup[] groups =
        [
            new() { Tabs = [new KeptTab { Program = @"C:\Apps\App.exe" }, new KeptTab { AppId = "Browser.Profile1" }], Front = 1 },
            new() { Tabs = [new KeptTab { AppId = "Mail" }, new KeptTab { AppId = "Notes" }, new KeptTab { AppId = "Mail" }], Front = 0 },
        ];

        _store.Save(groups);

        var read = _store.Load(out var readable);
        Assert.True(readable);
        Assert.Equal(2, read.Count);
        Assert.Equal(groups[0].Tabs, read[0]!.Tabs);
        Assert.Equal(1, read[0]!.Front);
        Assert.Equal(groups[1].Tabs, read[1]!.Tabs);
        Assert.False(File.Exists(_store.FilePath + ".tmp"));
        Assert.StartsWith("""{"version":1,"groups":[{"tabs":[{"appId":null,"program":"C:\\Apps\\App.exe"},{"appId":"Browser.Profile1","program":null}],"front":1}""", File.ReadAllText(_store.FilePath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("""{ "groups": { "tabs": [] } }""")]
    [InlineData("""{ "groups": [ { "tabs": null } ] }""")]
    public void AFileThatCantBeReadHasNoGroups(string contents)
    {
        File.WriteAllText(_store.FilePath, contents);

        Assert.Empty(_store.Load(out var readable));
        Assert.False(readable);
    }

    [Fact]
    public void AFileLargerThanTheLimitCantBeRead()
    {
        File.WriteAllText(_store.FilePath, new string(' ', KeptGroupsStore.MaximumFileSize + 1));

        Assert.Empty(_store.Load(out var readable));
        Assert.False(readable);
    }

    [Fact]
    public void EntriesArentCheckedWhenTheFileIsRead()
    {
        File.WriteAllText(_store.FilePath, """{ "groups": [ null, { "tabs": [ null, { "appId": "A", "program": "B" } ], "front": 9 } ] }""");

        var read = _store.Load(out var readable);

        Assert.True(readable);
        Assert.Null(read[0]);
        Assert.Equal(9, read[1]!.Front);
        Assert.Equal([null, new KeptTab { AppId = "A", Program = "B" }], read[1]!.Tabs);
    }

    [Fact]
    public void DeleteDeletesTheFileAndDoesNothingWithoutOne()
    {
        _store.Save([new KeptGroup { Tabs = [new KeptTab { AppId = "Mail" }, new KeptTab { AppId = "Notes" }] }]);

        _store.Delete();
        _store.Delete();
        new KeptGroupsStore(Path.Combine(_folder.FullPath, "Missing", KeptGroupsStore.FileName)).Delete();

        Assert.False(File.Exists(_store.FilePath));
    }
}
