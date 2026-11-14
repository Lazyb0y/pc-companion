using System;
using System.Globalization;
using System.IO;
using System.Linq;
using PCCompanion.Modules.Files.RemoteFolders;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class RemoteNamesTests
{
    [Theory]
    [InlineData("report.docx")]
    [InlineData(".bashrc")]
    [InlineData("Ünïcödé 密码.txt")]
    [InlineData("a b")]
    [InlineData("console.txt")]
    [InlineData("COM0")]
    public void OrdinaryNamesAreShown(string name)
    {
        Assert.Equal(HiddenReason.None, RemoteNames.Check(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("star*")]
    [InlineData("pipe|")]
    [InlineData("quote\"")]
    [InlineData("<angle>")]
    [InlineData("tab\t")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("COM1")]
    [InlineData("lpt9.log")]
    [InlineData("COM¹")]
    [InlineData("CONIN$")]
    [InlineData("conout$.txt")]
    [InlineData("ends with a dot.")]
    [InlineData("ends with a space ")]
    public void NamesWindowsDoesntAllowAreHidden(string name)
    {
        Assert.Equal(HiddenReason.InvalidName, RemoteNames.Check(name));
    }

    [Fact]
    public void TooLongANameIsHidden()
    {
        Assert.Equal(HiddenReason.None, RemoteNames.Check(new string('a', 255)));
        Assert.Equal(HiddenReason.TooLong, RemoteNames.Check(new string('a', 256)));
    }

    [Fact]
    public void ANameThatIsntUtf8IsHidden()
    {
        Assert.Equal(HiddenReason.NotUtf8, RemoteNames.Check("bad�name.txt"));
    }

    [Theory]
    [InlineData(".pcc-upload-1a2b3c4d")]
    [InlineData("report.docx.pcc-old-1a2b3c4d")]
    public void PCCompanionsOwnTemporaryFilesAreHidden(string name)
    {
        Assert.Equal(HiddenReason.OwnTemporaryFile, RemoteNames.Check(name));
    }

    [Fact]
    public void ASecondNameDifferingOnlyInCaseIsHiddenAndTheFirstShown()
    {
        var selection = RemoteNames.Select(["Readme.md", "README.md", "a:b", "notes.txt", "readme.MD"], name => name);

        Assert.Equal(["Readme.md", "notes.txt"], selection.Shown);
        Assert.Equal(2, selection.Hidden[HiddenReason.SameNameOtherCase]);
        Assert.Equal(1, selection.Hidden[HiddenReason.InvalidName]);
        Assert.Equal(3, selection.HiddenCount);
    }

    [Fact]
    public void AtMostTheMaximumIsShown()
    {
        var names = Enumerable.Range(0, 5).Select(i => $"f{i}").ToList();

        var selection = RemoteNames.Select(names, name => name, truncated: true, maximum: 3);

        Assert.Equal(["f0", "f1", "f2"], selection.Shown);
        Assert.Equal(3, selection.Hidden[HiddenReason.TooMany]);
    }

    [Fact]
    public void DotNamesAreHiddenOnThePc()
    {
        Assert.True(RemoteNames.IsHiddenOnPc(".config"));
        Assert.False(RemoteNames.IsHiddenOnPc("config"));
    }

    [Theory]
    [InlineData("~$Report.docx", true)]
    [InlineData("~WRL0001.tmp", true)]
    [InlineData("~wrd1234.TMP", true)]
    [InlineData(".~lock.Report.odt#", true)]
    [InlineData("Thumbs.db", true)]
    [InlineData("desktop.ini", true)]
    [InlineData("Desktop.INI", true)]
    [InlineData("Report.docx", false)]
    [InlineData("~notes.txt", false)]
    [InlineData(".~lock.Report.odt", false)]
    [InlineData("report.tmp", false)]
    public void ProgramsTemporaryFilesStayOnThePc(string name, bool stays)
    {
        Assert.Equal(stays, RemoteNames.StaysOnPc(name));
    }
}

public sealed class ItemIdentityTests
{
    private static readonly DateTime When = new(2026, 11, 14, 15, 30, 12, DateTimeKind.Utc);

    [Fact]
    public void AnIdentityIsReadBackFromItsBytes()
    {
        var identity = ItemIdentity.Of("docs/2026", "Report ünï.docx", isDirectory: false, 12_345, When);

        var read = ItemIdentity.FromBytes(identity.ToBytes());

        Assert.Equal(identity, read);
        Assert.Equal("docs/2026/Report ünï.docx", read?.RelativePath);
        Assert.Equal("Report ünï.docx", read?.Name);
    }

    [Fact]
    public void AFolderHasNoSize()
    {
        var folder = ItemIdentity.Of("", "docs", isDirectory: true, 4096, When);

        Assert.Equal(0, folder.Size);
        Assert.Equal("docs", folder.RelativePath);
        Assert.True(folder.IsSameVersion(1, DateTime.UnixEpoch));
    }

    [Fact]
    public void TheVersionIsTheSizeAndTime()
    {
        var file = ItemIdentity.Of("", "a.txt", isDirectory: false, 10, When);

        Assert.True(file.IsSameVersion(10, When));
        Assert.False(file.IsSameVersion(11, When));
        Assert.False(file.IsSameVersion(10, When.AddSeconds(1)));
    }

    [Fact]
    public void RemotePathsJoinTheServersFolder()
    {
        Assert.Equal("/home/ana/docs/a.txt", ItemIdentity.RemotePath("/home/ana", "docs/a.txt"));
        Assert.Equal("/home/ana/docs/a.txt", ItemIdentity.RemotePath("/home/ana/", "docs/a.txt"));
        Assert.Equal("/home/ana", ItemIdentity.RemotePath("/home/ana", ""));
        Assert.Equal("/a.txt", ItemIdentity.RemotePath("/", "a.txt"));
    }

    [Fact]
    public void APathTooLongForAPlaceholderIsRefused()
    {
        string longest = new('a', ItemIdentity.MaximumLength - 18);

        Assert.True(ItemIdentity.Fits(longest));
        Assert.False(ItemIdentity.Fits(longest + "a"));
        Assert.Throws<InvalidOperationException>(() => new ItemIdentity(longest + "a", false, 1, When).ToBytes());
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 97 })]
    [InlineData(new byte[] { 1, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 97 })]
    [InlineData(new byte[] { 1, 0, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0, 97 })]
    [InlineData(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF })]
    public void BytesOfAnotherFormatAreNoIdentity(byte[] bytes)
    {
        Assert.Null(ItemIdentity.FromBytes(bytes));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("docs/../..")]
    [InlineData("./a")]
    [InlineData("a//b")]
    [InlineData("a/")]
    public void PathsThatLeaveTheFolderAreNoIdentity(string path)
    {
        byte[] bytes = new ItemIdentity(path, false, 1, When).ToBytes();

        Assert.Null(ItemIdentity.FromBytes(bytes));
    }
}

public sealed class ConflictNamesTests
{
    private static readonly DateTime When = new(2026, 11, 14, 15, 30, 0, DateTimeKind.Local);

    private static string Name(string name, string culture) =>
        ConflictNames.For(name, When, CultureInfo.GetCultureInfo(culture), CultureInfo.GetCultureInfo(culture));

    [Fact]
    public void AConflictCopyKeepsTheExtension()
    {
        string name = Name("Report.docx", "en-US");

        Assert.StartsWith("Report (conflict ", name, StringComparison.Ordinal);
        Assert.EndsWith(").docx", name, StringComparison.Ordinal);
        Assert.Contains("2026", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("zh-CN")]
    [InlineData("bn-BD")]
    [InlineData("fr-FR")]
    [InlineData("es-ES")]
    public void ConflictNamesAreValidFileNamesInEveryLanguage(string culture)
    {
        string name = Name("Report.docx", culture);

        Assert.Equal(NamingProblem.None, FileNaming.CheckName(name));
        Assert.EndsWith(".docx", name, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWordsComeFromTheLanguage()
    {
        Assert.Contains("Konflikt", Name("a.txt", "de-DE"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWordsFollowTheLanguageAndTheDateTheRegionalFormats()
    {
        string name = ConflictNames.For("a.txt", When, CultureInfo.GetCultureInfo("de-DE"), CultureInfo.GetCultureInfo("en-US"));

        Assert.Contains("Konflikt", name, StringComparison.Ordinal);
        Assert.Contains("11-14-2026", name, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherConflictCopyGetsANumber()
    {
        string name = Name("Report.docx", "en-US");

        string second = ConflictNames.Numbered(name, 2);

        Assert.Equal(name[..^".docx".Length] + " 2.docx", second);
        Assert.Equal(NamingProblem.None, FileNaming.CheckName(second));
        Assert.Equal("Makefile 3", ConflictNames.Numbered("Makefile", 3));
    }

    [Fact]
    public void ANumberedLongNameStaysShortEnough()
    {
        string name = Name(new string('r', 250) + ".docx", "en-US");

        string numbered = ConflictNames.Numbered(name, 12);

        Assert.True(numbered.Length <= FileNaming.MaximumNameLength);
        Assert.EndsWith(" 12.docx", numbered, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameWithoutAnExtensionGetsNone()
    {
        string name = Name("Makefile", "en-US");

        Assert.StartsWith("Makefile (conflict ", name, StringComparison.Ordinal);
        Assert.EndsWith(")", name, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongNameIsShortenedBeforeTheExtension()
    {
        string name = Name(new string('r', 250) + ".docx", "en-US");

        Assert.True(name.Length <= FileNaming.MaximumNameLength);
        Assert.EndsWith(").docx", name, StringComparison.Ordinal);
        Assert.Equal(NamingProblem.None, FileNaming.CheckName(name));
    }

    [Fact]
    public void TheNameHasNoPathOfItsOwn()
    {
        Assert.Equal("a", Path.GetFileNameWithoutExtension("a.txt"));
        Assert.DoesNotContain('\\', Name("a.txt", "en-US"));
        Assert.DoesNotContain('/', Name("a.txt", "en-US"));
    }
}
