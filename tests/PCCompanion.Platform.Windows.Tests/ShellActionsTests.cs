using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

public sealed class ShellActionsTests
{
    // Refused before anything starts, so no browser or other program opens.
    [Theory]
    [InlineData("http://github.com/Lazyb0y/pc-companion/releases/latest")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    public void OnlyHttpsPagesAreOpened(string address)
    {
        Assert.Throws<ArgumentException>(() => ShellActions.OpenWebPage(new Uri(address)));
    }

    [Fact]
    public void RelativeAddressesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => ShellActions.OpenWebPage(new Uri("releases/latest", UriKind.Relative)));
        Assert.Throws<ArgumentException>(() => ShellActions.OpenLink(new Uri("releases/latest", UriKind.Relative)));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("search-ms:query=calc")]
    [InlineData("ftp://example.com/file")]
    public void LinksOpenOnlyForHttpAndHttps(string address)
    {
        Assert.Throws<ArgumentException>(() => ShellActions.OpenLink(new Uri(address)));
    }
}
