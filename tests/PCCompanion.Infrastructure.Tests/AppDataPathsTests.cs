using System;
using System.IO;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void ForCurrentUserUsesLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCCompanion");

        Assert.Equal(expected, AppDataPaths.ForCurrentUser().Root, ignoreCase: true);
    }

    [Fact]
    public void FilesAreInsideTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PCCompanion.Tests");
        var paths = new AppDataPaths(root);

        Assert.Equal(Path.Combine(root, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(root, "Logs"), paths.LogsFolder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PCCompanion")]
    [InlineData(@"..\PCCompanion")]
    [InlineData(@"\PCCompanion")]
    public void RootMustBeAbsolute(string root)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AppDataPaths(root));
    }
}
