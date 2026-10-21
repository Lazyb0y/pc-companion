using System.IO;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class UserDataRemovalTests
{
    [Fact]
    public void RemovesTheDataFolder()
    {
        using var folder = new TemporaryFolder();
        var paths = CreateData(folder);

        Assert.True(UserDataRemoval.Remove(paths, keepSettings: false));

        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void KeepingSettingsRemovesEverythingButTheSettingsFile()
    {
        using var folder = new TemporaryFolder();
        var paths = CreateData(folder);

        Assert.True(UserDataRemoval.Remove(paths, keepSettings: true));

        Assert.Equal([paths.SettingsFile], Directory.GetFileSystemEntries(paths.Root));
        Assert.Equal("{}", File.ReadAllText(paths.SettingsFile));
    }

    [Fact]
    public void KeepsOnlyTheSettingsFileAtTheTop()
    {
        using var folder = new TemporaryFolder();
        var paths = CreateData(folder);
        File.WriteAllText(Path.Combine(paths.LogsFolder, "settings.json"), "{}");

        Assert.True(UserDataRemoval.Remove(paths, keepSettings: true));

        Assert.False(Directory.Exists(paths.LogsFolder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingFolderIsAlreadyRemoved(bool keepSettings)
    {
        using var folder = new TemporaryFolder();
        var paths = new AppDataPaths(Path.Combine(folder.FullPath, "PCCompanion"));

        Assert.True(UserDataRemoval.Remove(paths, keepSettings));
    }

    [Fact]
    public void ReportsWhatCouldNotBeRemovedAndRemovesTheRest()
    {
        using var folder = new TemporaryFolder();
        var paths = CreateData(folder);
        var open = Path.Combine(paths.LogsFolder, "app-20261019.log");

        bool removed;
        using (File.Open(open, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            removed = UserDataRemoval.Remove(paths, keepSettings: false);
        }

        Assert.False(removed);
        Assert.True(File.Exists(open));
        Assert.False(File.Exists(Path.Combine(paths.LogsFolder, "host-20261019.log")));
        Assert.False(File.Exists(paths.SettingsFile));
    }

    // A data folder with settings, logs and another file, as PC Companion leaves it.
    private static AppDataPaths CreateData(TemporaryFolder folder)
    {
        var paths = new AppDataPaths(Path.Combine(folder.FullPath, "PCCompanion"));
        Directory.CreateDirectory(paths.LogsFolder);
        File.WriteAllText(paths.SettingsFile, "{}");
        File.WriteAllText(Path.Combine(paths.Root, "settings.json.tmp"), "{}");
        File.WriteAllText(Path.Combine(paths.LogsFolder, "app-20261019.log"), "log");
        File.WriteAllText(Path.Combine(paths.LogsFolder, "host-20261019.log"), "log");
        return paths;
    }
}
