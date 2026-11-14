using System.IO;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test writes shortcuts in a temporary folder of its own.
public sealed class ShellShortcutTests : System.IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void AShortcutReadsBackAsWritten()
    {
        var path = Path.Combine(_folder.FullPath, "Use Office - Network Profiles.lnk");
        var program = System.Environment.ProcessPath!;
        var content = new ShortcutContent(program, "--run networkProfiles use \"Office 2\"", "Network Profiles");

        ShellShortcut.Write(path, content);

        Assert.Equal(content, ShellShortcut.Read(path));
    }

    [Fact]
    public void WritingAgainReplacesTheShortcut()
    {
        var path = Path.Combine(_folder.FullPath, "Entry.lnk");
        var program = System.Environment.ProcessPath!;
        ShellShortcut.Write(path, new ShortcutContent(program, "--run hideApps showAll", "Hide Apps"));

        ShellShortcut.Write(path, new ShortcutContent(program, "--run endApp chooseApp", "End App"));

        Assert.Equal(new ShortcutContent(program, "--run endApp chooseApp", "End App"), ShellShortcut.Read(path));
    }

    [Fact]
    public void NamesInOtherScriptsKeepTheirCharacters()
    {
        var path = Path.Combine(_folder.FullPath, "استخدام مكتب - ملفات تعريف الشبكة.lnk");
        var program = System.Environment.ProcessPath!;
        var content = new ShortcutContent(program, "--run networkProfiles use مكتب", "ملفات تعريف الشبكة");

        ShellShortcut.Write(path, content);

        Assert.Equal(content, ShellShortcut.Read(path));
    }

    [Fact]
    public void AMissingOrBrokenShortcutReadsAsNone()
    {
        var broken = Path.Combine(_folder.FullPath, "Broken.lnk");
        File.WriteAllText(broken, "not a shortcut");

        Assert.Null(ShellShortcut.Read(Path.Combine(_folder.FullPath, "Missing.lnk")));
        Assert.Null(ShellShortcut.Read(broken));
    }
}
