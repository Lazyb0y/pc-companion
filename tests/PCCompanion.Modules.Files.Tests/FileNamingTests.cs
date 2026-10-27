using System;
using System.Globalization;
using System.Linq;
using PCCompanion.Modules.Files.FolderRules;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class FileNamingTests
{
    // Six hours ahead of UTC, so that a file modified late in the evening in UTC was modified the next day here.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(6), "Test", "Test");

    private static readonly SpecialFolders Places = new(
        @"C:\Windows",
        [@"C:\Program Files"],
        @"C:\Users\Ana\AppData\Local\PCCompanion",
        [@"C:\Users\Ana\AppData", @"C:\ProgramData"]);

    private static readonly FileFacts Invoice = new(
        "Invoice.pdf",
        1000,
        new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 5, 20, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("{date} {name}", "2026-03-06 Invoice.pdf")]
    [InlineData("{NAME} {Year}", "Invoice 2026.pdf")]
    [InlineData("{year}-{month}-{day}", "2026-03-06.pdf")]
    [InlineData("{name} ({extension})", "Invoice (pdf).pdf")]
    [InlineData("Paid", "Paid.pdf")]
    public void ANewNameHasTheFilesValuesAndKeepsTheExtension(string pattern, string name)
    {
        Assert.Equal(new Naming(name, NamingProblem.None), FileNaming.NewName(pattern, Invoice, Zone));
    }

    [Fact]
    public void DatesUseTheModifiedDateInTheUsersTimeZone()
    {
        Assert.Equal("2026-03-05.pdf", FileNaming.NewName("{date}", Invoice, TimeZoneInfo.Utc).Value);
        Assert.Equal("2026-03-06.pdf", FileNaming.NewName("{date}", Invoice, Zone).Value);
    }

    [Fact]
    public void NumbersAreInAsciiDigitsAndTheGregorianCalendarInEveryLanguage()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            Assert.Equal("2026-03-06.pdf", FileNaming.NewName("{date}", Invoice, Zone).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void AFileWithoutAnExtensionGetsNone()
    {
        Assert.Equal("README 2026", FileNaming.NewName("{name} {year}", Invoice with { Name = "README" }, Zone).Value);
    }

    [Theory]
    [InlineData("README", "{extension}", NamingProblem.EmptyName)]
    [InlineData("README", "{name} {extension}", NamingProblem.InvalidName)]
    [InlineData("a.nul", "{extension}", NamingProblem.InvalidName)]
    [InlineData("README", "{name}.", NamingProblem.InvalidName)]
    public void ANewNameWindowsDoesntAllowIsntUsed(string fileName, string pattern, NamingProblem problem)
    {
        Assert.Equal(Naming.Failed(problem), FileNaming.NewName(pattern, Invoice with { Name = fileName }, Zone));
    }

    [Fact]
    public void ANewNameMayHave255Characters()
    {
        var file = Invoice with { Name = new string('a', 240) + ".pdf" };

        Assert.Equal(255, FileNaming.NewName("{name}" + new string('b', 11), file, Zone).Value.Length);
        Assert.Equal(Naming.Failed(NamingProblem.NameTooLong), FileNaming.NewName("{name}" + new string('b', 12), file, Zone));
    }

    [Theory]
    [InlineData("{date} {name}", true)]
    [InlineData("Paid {name}", true)]
    [InlineData("Paid", true)]
    [InlineData("{nam}", false)]
    [InlineData("{name", false)]
    [InlineData("name}", false)]
    [InlineData("{{name}}", false)]
    [InlineData(@"Paid\{name}", false)]
    [InlineData("Paid: {name}", false)]
    [InlineData("CON", false)]
    [InlineData("Paid.", false)]
    public void ANewNamePatternMustBeAbleToMakeAName(string pattern, bool valid)
    {
        Assert.Equal(valid, FileNaming.IsNewNamePattern(pattern));
    }

    [Theory]
    [InlineData(@"D:\Invoices", @"D:\Invoices")]
    [InlineData(@"D:\Invoices\{year}\{month}", @"D:\Invoices\2026\03")]
    [InlineData(@"D:\Invoices\{year}\Paid", @"D:\Invoices\2026\Paid")]
    [InlineData(@"D:\By type\{extension} files", @"D:\By type\pdf files")]
    [InlineData(@"D:\Invoices {year}", @"D:\Invoices 2026")]
    [InlineData("D:/Invoices/{year}/", @"D:\Invoices\2026")]
    public void ADestinationHasTheFilesValues(string destination, string path)
    {
        Assert.Equal(new Naming(path, NamingProblem.None), FileNaming.Destination(destination, Invoice, Zone, @"C:\Users\Ana\Downloads", Places));
    }

    [Fact]
    public void ASubfolderWithAnEmptyPartIsLeftOut()
    {
        var readme = Invoice with { Name = "README" };

        Assert.Equal(@"D:\By type", FileNaming.Destination(@"D:\By type\{extension}", readme, Zone, @"C:\Users\Ana\Downloads", Places).Value);
        Assert.Equal(@"D:\By type\2026", FileNaming.Destination(@"D:\By type\Type {extension}\{year}", readme, Zone, @"C:\Users\Ana\Downloads", Places).Value);
    }

    [Fact]
    public void ADestinationThatEndsUpInTheRulesOwnFolderIsntUsed()
    {
        var readme = Invoice with { Name = "README" };

        Assert.Equal(
            Naming.Failed(NamingProblem.DestinationIsOwnFolder),
            FileNaming.Destination(@"C:\Users\Ana\Downloads\{extension}", readme, Zone, @"C:\Users\Ana\Downloads", Places));
    }

    [Theory]
    [InlineData("..pdf")]
    [InlineData("CON .pdf")]
    public void ValuesFromTheFileCantLeaveTheDestination(string fileName)
    {
        Assert.Equal(
            Naming.Failed(NamingProblem.InvalidName),
            FileNaming.Destination(@"D:\Sorted\{name}", Invoice with { Name = fileName }, Zone, @"C:\Users\Ana\Downloads", Places));
    }

    [Fact]
    public void ADestinationMadeFromTheFileIsCheckedLikeAnyFolder()
    {
        Assert.Equal(
            Naming.Failed(NamingProblem.DestinationNotAllowed),
            FileNaming.Destination(@"C:\Users\Ana\AppData\Local\{name}", Invoice with { Name = "PCCompanion.zip" }, Zone, @"C:\Users\Ana\Downloads", Places));
    }

    [Theory]
    [InlineData(@"D:\Invoices", true)]
    [InlineData(@"D:\Invoices\{year}\{month}", true)]
    [InlineData(@"D:\{extension} files", true)]
    [InlineData(@"D:\{nam}", false)]
    [InlineData(@"D:\{year", false)]
    [InlineData(@"D:\{year}\..", false)]
    [InlineData(@"D:\{year}\a|b", false)]
    public void ADestinationsSubfoldersArePatterns(string destination, bool valid)
    {
        Assert.Equal(valid, FileNaming.IsDestinationPattern(destination));
    }

    [Fact]
    public void ANumberGoesBeforeTheExtension()
    {
        Assert.Equal(["report.pdf", "report (2).pdf", "report (3).pdf"], FileNaming.Candidates("report.pdf", NameConflict.AddNumber).Take(3));
        Assert.Equal(["README", "README (2)"], FileNaming.Candidates("README", NameConflict.AddNumber).Take(2));
        Assert.Equal(FileNaming.MaximumNumber, FileNaming.Candidates("report.pdf", NameConflict.AddNumber).Count());
    }

    [Fact]
    public void SkippingTriesOnlyTheName()
    {
        Assert.Equal(["report.pdf"], FileNaming.Candidates("report.pdf", NameConflict.Skip));
    }

    [Fact]
    public void ANumberThatMakesTheNameTooLongIsntTried()
    {
        var name = new string('a', 247) + ".pdf";
        var longer = new string('a', 248) + ".pdf";

        Assert.Equal(9, FileNaming.Candidates(name, NameConflict.AddNumber).Count());
        Assert.Equal(new string('a', 247) + " (9).pdf", FileNaming.Candidates(name, NameConflict.AddNumber).Last());
        Assert.Equal([longer], FileNaming.Candidates(longer, NameConflict.AddNumber));
    }

    [Fact]
    public void TheFirstFreeNameIsUsed()
    {
        string[] taken = [@"D:\Sorted\report.pdf", @"D:\Sorted\report (2).pdf"];

        Assert.Equal(@"D:\Sorted\report (3).pdf", FileNaming.FirstFree(@"D:\Sorted", "report.pdf", NameConflict.AddNumber, taken.Contains));
        Assert.Null(FileNaming.FirstFree(@"D:\Sorted", "report.pdf", NameConflict.Skip, taken.Contains));
        Assert.Equal(@"D:\Sorted\notes.txt", FileNaming.FirstFree(@"D:\Sorted", "notes.txt", NameConflict.Skip, taken.Contains));
    }

    [Fact]
    public void ARenameMayChangeOnlyTheCase()
    {
        static bool Exists(string path) => string.Equals(path, @"D:\Sorted\report.pdf", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(@"D:\Sorted\Report.pdf", FileNaming.FirstFree(@"D:\Sorted", "Report.pdf", NameConflict.Skip, Exists, @"D:\Sorted\report.pdf"));
    }
}
