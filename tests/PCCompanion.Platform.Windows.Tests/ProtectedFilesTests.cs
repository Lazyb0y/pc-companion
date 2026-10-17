using System;
using System.IO;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ProtectedFilesTests
{
    [Fact]
    public void TheWindowsSystemFolderAndItsProgramsAreProtected()
    {
        Assert.True(ProtectedFiles.IsProtected(Environment.SystemDirectory));
        Assert.True(ProtectedFiles.IsProtected(Path.Combine(Environment.SystemDirectory, "schtasks.exe")));
    }

    [Fact]
    public void AFolderTheUserCreatedIsNotProtected()
    {
        var folder = Directory.CreateTempSubdirectory("PCCompanion.Tests.").FullName;
        try
        {
            var file = Path.Combine(folder, "Host.exe");
            File.WriteAllBytes(file, []);

            Assert.False(ProtectedFiles.IsProtected(folder));
            Assert.False(ProtectedFiles.IsProtected(file));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AUserFolderIsNotCreatedInAFolderOthersCanChange()
    {
        var parent = Directory.CreateTempSubdirectory("PCCompanion.Tests.").FullName;
        try
        {
            var folder = Path.Combine(parent, "S-1-5-21-1111111111-2222222222-3333333333-1001");

            Assert.False(ProtectedFiles.CreateUserFolder(folder, Elevation.CurrentUserSid));
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void AMissingPathIsNotProtected()
    {
        Assert.False(ProtectedFiles.IsProtected(Path.Combine(Path.GetTempPath(), $"PCCompanion.Tests.{Guid.NewGuid():N}")));
    }
}
