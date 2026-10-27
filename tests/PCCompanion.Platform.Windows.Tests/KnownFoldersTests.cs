using System;
using System.IO;
using Microsoft.Win32;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class KnownFoldersTests
{
    [Fact]
    public void DownloadsIsWhereWindowsKeepsTheUsersDownloads()
    {
        // Windows keeps the folder's place, which the user can change, with the Downloads folder's ID.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        var place = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
        var expected = place is null ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") : Environment.ExpandEnvironmentVariables(place);

        Assert.Equal(Path.TrimEndingDirectorySeparator(expected), KnownFolders.Downloads, StringComparer.OrdinalIgnoreCase);
    }
}
