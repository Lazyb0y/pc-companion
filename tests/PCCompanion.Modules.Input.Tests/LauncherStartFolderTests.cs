using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Input.LauncherActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

// Each test uses a Start folder of programs of its own, in a temporary folder.
public sealed class LauncherStartFolderTests : IDisposable
{
    private const string Program = @"C:\Program Files\PC Companion\PCCompanion.exe";
    private const string FolderName = "PC Companion actions";

    private static readonly StartEntry UseOffice = new("Use Office - Network Profiles", "--run networkProfiles use Office", "Network Profiles");
    private static readonly StartEntry ShowAll = new("Show all hidden windows - Hide Apps", "--run hideApps showAll", "Hide Apps");

    private readonly TemporaryFolder _temporary = new();
    private readonly FakeShortcutFiles _shortcuts = new();
    private readonly LauncherStartFolder _start;

    public LauncherStartFolderTests()
    {
        Directory.CreateDirectory(Programs);
        _start = new LauncherStartFolder(Programs, _shortcuts);
    }

    private string Programs => Path.Combine(_temporary.FullPath, "Programs");

    private string Folder => Path.Combine(Programs, FolderName);

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void TheEntriesAreWrittenInTheirFolderWithItsMark()
    {
        Assert.Equal(2, _start.Update(FolderName, [UseOffice, ShowAll], Program));

        Assert.Equal(
            [new ShortcutContent(Program, "--run hideApps showAll", "Hide Apps"), new ShortcutContent(Program, "--run networkProfiles use Office", "Network Profiles")],
            Shortcuts().Select(path => _shortcuts.Files[path]));
        Assert.Equal(
            ["Show all hidden windows - Hide Apps.lnk", "Use Office - Network Profiles.lnk"],
            Shortcuts().Select(Path.GetFileName));
        var marker = new FileInfo(Path.Combine(Folder, LauncherStartFolder.MarkerName));
        Assert.True(marker.Attributes.HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void AnUpdateThatChangesNothingWritesNothing()
    {
        _start.Update(FolderName, [UseOffice, ShowAll], Program);

        Assert.Equal(0, _start.Update(FolderName, [UseOffice, ShowAll], Program));
        Assert.Equal(2, _shortcuts.Writes);
    }

    [Fact]
    public void AnEntryThatChangedIsWrittenAgainAndOneNoLongerListedIsDeleted()
    {
        _start.Update(FolderName, [UseOffice, ShowAll], Program);

        Assert.Equal(1, _start.Update(FolderName, [UseOffice with { Arguments = "--run networkProfiles use \"Office 2\"" }], Program));

        var path = Assert.Single(Shortcuts());
        Assert.Equal("--run networkProfiles use \"Office 2\"", _shortcuts.Files[path].Arguments);
    }

    [Fact]
    public void AnEntryForAnotherProgramIsWrittenAgain()
    {
        _start.Update(FolderName, [UseOffice], @"C:\Old\PCCompanion.exe");

        Assert.Equal(1, _start.Update(FolderName, [UseOffice], Program));
    }

    [Fact]
    public void OtherFilesInTheFolderAreDeleted()
    {
        _start.Update(FolderName, [UseOffice], Program);
        File.WriteAllText(Path.Combine(Folder, "notes.txt"), "mine");
        Directory.CreateDirectory(Path.Combine(Folder, "More", "Deeper"));
        File.WriteAllText(Path.Combine(Folder, "More", "Deeper", "file.txt"), "x");

        _start.Update(FolderName, [UseOffice], Program);

        Assert.Equal(["Use Office - Network Profiles.lnk"], Directory.EnumerateFileSystemEntries(Folder).Select(Path.GetFileName).Where(name => name != LauncherStartFolder.MarkerName));
    }

    [Fact]
    public void ALinkInTheFolderIsDeletedButNotWhatItLeadsTo()
    {
        var elsewhere = Path.Combine(_temporary.FullPath, "Elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "keep.txt"), "keep");
        _start.Update(FolderName, [UseOffice], Program);
        Assert.SkipUnless(TryLink(Path.Combine(Folder, "Link"), elsewhere), "Creating a symbolic link needs Developer Mode or admin rights.");

        _start.Update(FolderName, [UseOffice], Program);

        Assert.False(Directory.Exists(Path.Combine(Folder, "Link")));
        Assert.True(File.Exists(Path.Combine(elsewhere, "keep.txt")));
    }

    [Fact]
    public void AFolderThatIsALinkIsReplacedRatherThanWrittenThrough()
    {
        var elsewhere = Path.Combine(_temporary.FullPath, "Elsewhere");
        Directory.CreateDirectory(elsewhere);
        Assert.SkipUnless(TryLink(Folder, elsewhere), "Creating a symbolic link needs Developer Mode or admin rights.");

        _start.Update(FolderName, [UseOffice], Program);

        Assert.False(new DirectoryInfo(Folder).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
        Assert.Single(Shortcuts());
    }

    [Fact]
    public void ANewNameRenamesTheMarkedFolder()
    {
        _start.Update(FolderName, [UseOffice], Program);

        Assert.Equal(1, _start.Update("Aktionen von PC Companion", [UseOffice with { Name = "Office verwenden - Netzwerkprofile" }], Program));

        Assert.False(Directory.Exists(Folder));
        Assert.Equal(["Office verwenden - Netzwerkprofile.lnk"], Directory.EnumerateFiles(Path.Combine(Programs, "Aktionen von PC Companion"), "*.lnk").Select(Path.GetFileName));
    }

    [Fact]
    public void OtherFoldersInStartAreLeftAlone()
    {
        var other = Path.Combine(Programs, "Accessories");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Notepad.lnk"), "not ours");

        _start.Update(FolderName, [UseOffice], Program);
        _start.Update("Renamed", [UseOffice], Program);
        _start.Delete();

        Assert.Equal("not ours", File.ReadAllText(Path.Combine(other, "Notepad.lnk")));
        Assert.Equal(["Accessories"], Directory.EnumerateDirectories(Programs).Select(Path.GetFileName));
    }

    [Fact]
    public void NoEntriesDeleteTheFolder()
    {
        _start.Update(FolderName, [UseOffice], Program);

        Assert.Equal(0, _start.Update(FolderName, [], Program));

        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void DeletingWithoutAFolderDoesNothing()
    {
        _start.Delete();

        new LauncherStartFolder(Path.Combine(_temporary.FullPath, "No Start"), _shortcuts).Delete();
    }

    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException)
        {
            // Symbolic links need Developer Mode or admin rights.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private List<string> Shortcuts() => [.. Directory.EnumerateFiles(Folder, "*.lnk").Order(StringComparer.Ordinal)];

    // Keeps each shortcut's content by its path, and writes the file, empty, as Windows would.
    private sealed class FakeShortcutFiles : IShortcutFiles
    {
        public Dictionary<string, ShortcutContent> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }

        public ShortcutContent? Read(string path) => Files.TryGetValue(path, out var content) ? content : null;

        public void Write(string path, ShortcutContent content)
        {
            File.WriteAllBytes(path, []);
            Files[path] = content;
            Writes++;
        }
    }
}
