using System;
using PCCompanion.Core;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

// The arguments a host running as administrator passes to its copy without admin rights.
public sealed class WebPageArgumentsTests
{
    [Theory]
    [InlineData("https://tracking.example.com/?number=1Z999AA10123456784")]
    [InlineData("http://example.com/a b\"c")]
    [InlineData("https://example.com/search?q=r%C3%A9sum%C3%A9&lang=fr#top")]
    [InlineData("https://bücher.example/straße")]
    [InlineData(@"https://example.com/back\slash")]
    public void TheCopyReadsTheAddressTheHostPasses(string text)
    {
        var address = new Uri(text);

        var arguments = HostProcess.WebPageArguments(address);
        // Split as Windows splits a command line without quotes.
        var read = HostProcess.ReadWebPageAddress(arguments.Split(' '));

        Assert.Equal(2, arguments.Split(' ').Length);
        Assert.Equal(address.AbsoluteUri, read?.AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("search-ms:query=calc")]
    public void OnlyWebAddressesArePassed(string text)
    {
        Assert.Throws<ArgumentException>(() => HostProcess.WebPageArguments(new Uri(text)));
    }

    [Theory]
    [InlineData("--open-web-page", "file:///C:/Windows/System32/calc.exe")]
    [InlineData("--open-web-page", "ms-settings:privacy")]
    [InlineData("--open-web-page", "example.com")]
    [InlineData("--open-web-page")]
    [InlineData("--open-release-page", "https://example.com")]
    public void TheCopyOpensNothingButAWebAddress(params string[] arguments)
    {
        Assert.Null(HostProcess.ReadWebPageAddress(arguments));
    }

    [Fact]
    public void WebAddressesAreAbsoluteHttpOrHttps()
    {
        Assert.True(HostProcess.IsWebAddress(new Uri("https://example.com")));
        Assert.True(HostProcess.IsWebAddress(new Uri("http://example.com")));
        Assert.False(HostProcess.IsWebAddress(new Uri("ftp://example.com")));
        Assert.False(HostProcess.IsWebAddress(new Uri("example", UriKind.Relative)));
        Assert.False(HostProcess.IsWebAddress(null));
    }
}
