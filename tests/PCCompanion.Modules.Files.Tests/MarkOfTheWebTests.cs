using System;
using System.IO;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

public sealed class MarkOfTheWebTests
{
    [Fact]
    public void AMarkFromAPageGivesTheFilesAddress()
    {
        var mark = MarkOfTheWeb.Parse("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://www.example.com/downloads\r\nHostUrl=https://files.example.com/report.pdf\r\n");

        Assert.Equal(3, mark.ZoneId);
        Assert.Equal(new Uri("https://www.example.com/downloads"), mark.ReferrerUrl);
        Assert.Equal(new Uri("https://files.example.com/report.pdf"), mark.Source);
    }

    [Fact]
    public void WithoutAWebAddressToDownloadFromThePageCounts()
    {
        var mark = MarkOfTheWeb.Parse("[ZoneTransfer]\nZoneId=3\nReferrerUrl=https://www.example.com/\nHostUrl=blob:https://www.example.com/1234\n");

        Assert.Equal(new Uri("https://www.example.com/"), mark.Source);
    }

    [Theory]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=about:internet\r\n")]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\n")]
    [InlineData("")]
    [InlineData("ZoneId=3\r\nHostUrl=https://example.com/a.pdf\r\n")]
    [InlineData("[Other]\r\nHostUrl=https://example.com/a.pdf\r\n")]
    [InlineData("[ZoneTransfer]\r\nHostUrl=file:///C:/Users/Ana/a.pdf\r\n")]
    [InlineData("[ZoneTransfer]\r\nHostUrl=not an address\r\nHostUrl\r\n=https://example.com/\r\n")]
    public void AMarkWithoutAWebAddressNamesNoSite(string text)
    {
        Assert.Null(MarkOfTheWeb.Parse(text).Source);
    }

    [Fact]
    public void NamesIgnoreCaseAndTheFirstValueCounts()
    {
        var mark = MarkOfTheWeb.Parse("[zonetransfer]\r\n zoneid = 3 \r\nHOSTURL=https://one.example.com/\r\nHostUrl=https://two.example.com/\r\n");

        Assert.Equal(3, mark.ZoneId);
        Assert.Equal("one.example.com", mark.Source?.Host);
    }

    [Fact]
    public void AZoneThatIsntANumberIsLeftOut()
    {
        Assert.Null(MarkOfTheWeb.Parse("[ZoneTransfer]\r\nZoneId=-1\r\n").ZoneId);
        Assert.Null(MarkOfTheWeb.Parse("[ZoneTransfer]\r\nZoneId=three\r\n").ZoneId);
    }

    // On files of its own in a folder of its own.
    [Fact]
    public void AFilesMarkIsReadFromItsStream()
    {
        using var folder = new TemporaryFolder();
        var marked = Path.Join(folder.FullPath, "marked.pdf");
        var plain = Path.Join(folder.FullPath, "plain.pdf");
        File.WriteAllText(marked, "pdf");
        File.WriteAllText(plain, "pdf");
        File.WriteAllText(marked + ":" + MarkOfTheWeb.StreamName, "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://downloads.example.com/file\r\n");

        Assert.Equal("downloads.example.com", MarkOfTheWeb.Read(marked)?.Source?.Host);
        Assert.Null(MarkOfTheWeb.Read(plain));
        Assert.Null(MarkOfTheWeb.Read(Path.Join(folder.FullPath, "missing.pdf")));
    }
}
