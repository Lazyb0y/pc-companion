using System;
using System.Text;
using PCCompanion.Core;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

// What a host running as administrator passes to its copy without admin rights to open a web page:
// the name of a pipe on the command line, and the address through the pipe.
public sealed class WebPageArgumentsTests
{
    private const string PipeName = "PCCompanion.OpenWebPage.1.0123456789abcdef0123456789abcdef";

    [Fact]
    public void TheCopyReadsThePipeTheHostPasses()
    {
        var arguments = HostProcess.WebPageArguments(PipeName);

        // Split as Windows splits a command line without quotes.
        Assert.Equal(["--open-web-page", PipeName], arguments.Split(' '));
        Assert.Equal(PipeName, HostProcess.ReadWebPagePipe(arguments.Split(' ')));
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData(@"..\other")]
    public void OnlyPipeNamesArePassed(string name)
    {
        Assert.Throws<ArgumentException>(() => HostProcess.WebPageArguments(name));
    }

    [Theory]
    [InlineData("--open-web-page")]
    [InlineData("--open-web-page", "https://example.com")]
    [InlineData("--run-program", PipeName)]
    public void TheCopyReadsNothingButAPipeName(params string[] arguments)
    {
        Assert.Null(HostProcess.ReadWebPagePipe(arguments));
    }

    [Theory]
    [InlineData("https://tracking.example.com/?number=1Z999AA10123456784")]
    [InlineData("http://example.com/a b\"c")]
    [InlineData("https://example.com/search?q=r%C3%A9sum%C3%A9&lang=fr#top")]
    [InlineData("https://bücher.example/straße")]
    [InlineData(@"https://example.com/back\slash")]
    public void TheAddressArrivesAsSent(string text)
    {
        var address = new Uri(text);

        var read = HostProcess.ReadWebAddress(HostProcess.WebAddressHandOff(address));

        Assert.Equal(address.AbsoluteUri, read?.AbsoluteUri);
    }

    [Fact]
    public void ALongAddressArrivesWhole()
    {
        // Copied text in a Clipboard Actions rule's address, far longer than a command line the copy can get.
        var address = new Uri("https://example.com/search?q=" + Uri.EscapeDataString(new string('é', 4096)));

        var read = HostProcess.ReadWebAddress(HostProcess.WebAddressHandOff(address));

        Assert.True(address.AbsoluteUri.Length > 20_000);
        Assert.Equal(address.AbsoluteUri, read?.AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("search-ms:query=calc")]
    public void OnlyWebAddressesAreHandedOff(string text)
    {
        Assert.Throws<ArgumentException>(() => HostProcess.WebAddressHandOff(new Uri(text)));
    }

    [Fact]
    public void AnAddressTooLongForTheBrowserIsntHandedOff()
    {
        var address = new Uri("https://example.com/?q=" + new string('a', HostProcess.MaximumWebAddressLength));

        Assert.Throws<ArgumentException>(() => HostProcess.WebAddressHandOff(address));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("example.com")]
    [InlineData("")]
    public void TheCopyOpensNothingButAWebAddress(string text)
    {
        Assert.Null(HostProcess.ReadWebAddress(Encoding.UTF8.GetBytes(text)));
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
