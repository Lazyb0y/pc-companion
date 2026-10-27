using System;
using PCCompanion.Modules.Files.FolderRules;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class RuleMatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly FileFacts Invoice = new("Invoice-0042.PDF", 250 * 1024, Now.AddDays(-40), Now.AddDays(-3));

    private static readonly MarkOfTheWeb BillingMark =
        MarkOfTheWeb.Parse("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://pay.billing.example.com/invoice.pdf\r\n");

    private static bool Matches(FolderRule rule, FileFacts file, MarkOfTheWeb? mark = null) =>
        RuleMatcher.Create(rule, 1).Matches(file, Now, () => mark);

    [Theory]
    [InlineData("invoice*")]
    [InlineData("INVOICE-????.pdf")]
    [InlineData("*.pdf")]
    [InlineData("receipt*; *0042*")]
    public void ANamePatternMatchesTheWholeNameIgnoringCase(string patterns)
    {
        Assert.True(Matches(new FolderRule { Names = patterns.Split(';') }, Invoice));
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("*.pdf.zip")]
    [InlineData("Invoice-???.pdf")]
    public void ANamePatternThatDoesntCoverTheWholeNameDoesntMatch(string pattern)
    {
        Assert.False(Matches(new FolderRule { Names = [pattern] }, Invoice));
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData(".PDF", true)]
    [InlineData(" pdf ", true)]
    [InlineData("doc", false)]
    public void TheTypeMatchesTheExtensionIgnoringCase(string extension, bool matches)
    {
        Assert.Equal(matches, Matches(new FolderRule { Extensions = ["txt", extension] }, Invoice));
    }

    [Fact]
    public void AFileWithoutAnExtensionHasNoType()
    {
        Assert.False(Matches(new FolderRule { Extensions = ["pdf"] }, Invoice with { Name = "README" }));
        Assert.Equal(string.Empty, (Invoice with { Name = "README." }).Extension);
    }

    [Fact]
    public void TheTypeIsTheLastExtension()
    {
        var archive = Invoice with { Name = "backup.tar.gz" };

        Assert.Equal("gz", archive.Extension);
        Assert.Equal("backup.tar", archive.BaseName);
        Assert.True(Matches(new FolderRule { Extensions = ["gz"] }, archive));
    }

    [Theory]
    [InlineData(200, SizeUnit.KB, null, null, true)]
    [InlineData(250, SizeUnit.KB, null, null, false)]
    [InlineData(null, null, 251, SizeUnit.KB, true)]
    [InlineData(null, null, 250, SizeUnit.KB, false)]
    [InlineData(100, SizeUnit.KB, 1, SizeUnit.MB, true)]
    [InlineData(1, SizeUnit.MB, 1, SizeUnit.GB, false)]
    public void SizesAreInBinaryUnits(int? larger, SizeUnit? largerUnit, int? smaller, SizeUnit? smallerUnit, bool matches)
    {
        var size = new SizeCondition
        {
            LargerThan = larger is { } l ? new SizeLimit { Amount = l, Unit = largerUnit!.Value } : null,
            SmallerThan = smaller is { } s ? new SizeLimit { Amount = s, Unit = smallerUnit!.Value } : null,
        };

        Assert.Equal(matches, Matches(new FolderRule { Size = size }, Invoice));
    }

    [Theory]
    [InlineData(FileDate.Created, 30, null, true)]
    [InlineData(FileDate.Created, 40, null, false)]
    [InlineData(FileDate.Modified, 30, null, false)]
    [InlineData(FileDate.Modified, null, 7, true)]
    [InlineData(FileDate.Modified, 1, 7, true)]
    [InlineData(FileDate.Created, null, 7, false)]
    public void TheDateIsMoreOrLessThanSomeDaysAgo(FileDate which, int? more, int? less, bool matches)
    {
        var date = new DateCondition { Which = which, MoreThanDaysAgo = more, LessThanDaysAgo = less };

        Assert.Equal(matches, Matches(new FolderRule { Date = date }, Invoice));
    }

    [Theory]
    [InlineData("*.billing.example.com", true)]
    [InlineData("pay.billing.example.com", true)]
    [InlineData("*.EXAMPLE.com", true)]
    [InlineData("billing.example.com", false)]
    [InlineData("*.example.org", false)]
    public void DownloadedFromMatchesTheSiteInTheMark(string pattern, bool matches)
    {
        Assert.Equal(matches, Matches(new FolderRule { DownloadedFrom = [pattern] }, Invoice, BillingMark));
    }

    [Fact]
    public void DownloadedFromMatchesASiteInOtherLettersInEitherForm()
    {
        var mark = MarkOfTheWeb.Parse("[ZoneTransfer]\r\nHostUrl=https://shop.münchen.example/a.pdf\r\n");

        Assert.True(Matches(new FolderRule { DownloadedFrom = ["*.münchen.example"] }, Invoice, mark));
        Assert.True(Matches(new FolderRule { DownloadedFrom = ["*.xn--mnchen-3ya.example"] }, Invoice, mark));
    }

    [Fact]
    public void AFileWithoutAMarkOrASiteInItIsntDownloadedFromAnywhere()
    {
        var rule = new FolderRule { DownloadedFrom = ["*"] };

        Assert.False(Matches(rule, Invoice));
        Assert.False(Matches(rule, Invoice, MarkOfTheWeb.Parse("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=about:internet\r\n")));
        Assert.True(Matches(rule, Invoice, BillingMark));
    }

    [Fact]
    public void AllConditionsMustMatch()
    {
        var rule = new FolderRule { Names = ["invoice*"], Extensions = ["pdf"], DownloadedFrom = ["*.billing.example.com"] };

        Assert.True(Matches(rule, Invoice, BillingMark));
        Assert.False(Matches(rule, Invoice with { Name = "Invoice-0042.docx" }, BillingMark));
        Assert.False(Matches(rule, Invoice));
    }

    [Fact]
    public void TheFirstRuleThatMatchesActs()
    {
        var rules = new[]
        {
            RuleMatcher.Create(new FolderRule { Name = "Pictures", Extensions = ["png"] }, 1),
            RuleMatcher.Create(new FolderRule { Name = "Invoices", Names = ["invoice*"] }, 2),
            RuleMatcher.Create(new FolderRule { Name = "PDFs", Extensions = ["pdf"] }, 3),
        };

        Assert.Equal(2, RuleMatcher.FirstMatch(rules, Invoice, Now, () => null)?.Number);
        Assert.Null(RuleMatcher.FirstMatch(rules, Invoice with { Name = "notes.txt" }, Now, () => null));
    }

    [Fact]
    public void TheMarkIsReadOnceAndOnlyWhenTheOtherConditionsMatch()
    {
        var reads = 0;
        var rules = new[]
        {
            RuleMatcher.Create(new FolderRule { Extensions = ["png"], DownloadedFrom = ["*"] }, 1),
            RuleMatcher.Create(new FolderRule { Extensions = ["pdf"], DownloadedFrom = ["*.example.org"] }, 2),
            RuleMatcher.Create(new FolderRule { Extensions = ["pdf"], DownloadedFrom = ["*.example.com"] }, 3),
        };

        var match = RuleMatcher.FirstMatch(rules, Invoice, Now, () =>
        {
            reads++;
            return BillingMark;
        });

        Assert.Equal(3, match?.Number);
        Assert.Equal(1, reads);
        Assert.Null(RuleMatcher.FirstMatch(rules, Invoice with { Name = "notes.txt" }, Now, () => throw new InvalidOperationException("The mark was read.")));
    }

    [Fact]
    public void TheEditorSeesEachConditionThatsOn()
    {
        var rule = new FolderRule
        {
            Names = ["invoice*"],
            Extensions = ["docx"],
            Size = new SizeCondition { SmallerThan = new SizeLimit { Amount = 1, Unit = SizeUnit.MB } },
            DownloadedFrom = ["*.example.org"],
        };

        var results = RuleMatcher.Create(rule, 1).Explain(Invoice, Now, () => BillingMark);

        Assert.Equal(
            [
                new ConditionResult(RuleCondition.Name, true),
                new ConditionResult(RuleCondition.Type, false),
                new ConditionResult(RuleCondition.Size, true),
                new ConditionResult(RuleCondition.DownloadedFrom, false),
            ],
            results);
    }

    [Fact]
    public void EveryKindHasExtensionsTheTypeConditionAccepts()
    {
        foreach (var kind in Enum.GetValues<FileKind>())
        {
            var extensions = FileKinds.Extensions(kind);

            Assert.NotEmpty(extensions);
            Assert.Equal(RuleProblem.None, FolderRuleCheck.CheckExtensions(extensions));
        }
    }
}
