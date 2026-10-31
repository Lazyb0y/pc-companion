using System;
using System.IO;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// Each test uses a folder of its own under the temp folder.
public sealed class RememberedLanguagesStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly RememberedLanguagesStore _store;

    public RememberedLanguagesStoreTests()
    {
        _store = new RememberedLanguagesStore(Path.Combine(_folder.FullPath, RememberedLanguagesStore.FileName));
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void WithoutAFileThereAreNoApps()
    {
        Assert.Empty(_store.Load(out var readable));
        Assert.True(readable);
    }

    [Fact]
    public void SavedAppsAreReadBackInTheirOrder()
    {
        RememberedLanguage[] apps =
        [
            new() { Program = @"C:\Apps\App.exe", InputMethod = "0407:00000407" },
            new() { AppId = "Browser.Profile1", InputMethod = "0411" },
        ];

        _store.Save(apps);

        Assert.Equal(apps, _store.Load(out var readable));
        Assert.True(readable);
        Assert.False(File.Exists(_store.FilePath + ".tmp"));
        Assert.StartsWith("""{"version":1,"apps":[{"appId":null,"program":"C:\\Apps\\App.exe","inputMethod":"0407:00000407"}""", File.ReadAllText(_store.FilePath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("""{ "apps": { "program": "C:\\Apps\\App.exe" } }""")]
    public void AFileThatCantBeReadHasNoApps(string contents)
    {
        File.WriteAllText(_store.FilePath, contents);

        Assert.Empty(_store.Load(out var readable));
        Assert.False(readable);
    }

    [Fact]
    public void AFileLargerThanTheLimitCantBeRead()
    {
        File.WriteAllText(_store.FilePath, new string(' ', RememberedLanguagesStore.MaximumFileSize + 1));

        Assert.Empty(_store.Load(out var readable));
        Assert.False(readable);
    }

    [Fact]
    public void DeleteDeletesTheFileAndDoesNothingWithoutOne()
    {
        _store.Save([new RememberedLanguage { Program = @"C:\Apps\App.exe", InputMethod = "0407:00000407" }]);

        _store.Delete();
        _store.Delete();
        new RememberedLanguagesStore(Path.Combine(_folder.FullPath, "Missing", RememberedLanguagesStore.FileName)).Delete();

        Assert.False(File.Exists(_store.FilePath));
    }
}
