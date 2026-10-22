using PCCompanion.Modules.Network.NetworkProfiles;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Network.Tests;

public sealed class ProxyMatchingTests
{
    private static readonly ProxyProfile Office = new()
    {
        UseServer = true,
        Server = "proxy.example.com:8080",
        Exceptions = ["*.example.com", "10.*"],
        BypassLocal = true,
    };

    [Fact]
    public void AProfileIsWrittenWithLocalAddressesLast()
    {
        Assert.Equal(
            new ProxySettings(false, false, "", true, "proxy.example.com:8080", "*.example.com;10.*;<local>"),
            ProxyMatching.ToWindows(Office));
    }

    [Fact]
    public void LocalAddressesAmongTheExceptionsAreWrittenOnce()
    {
        var profile = Office with { Exceptions = ["<LOCAL>", " *.example.com "], BypassLocal = true };

        Assert.Equal("*.example.com;<local>", ProxyMatching.ToWindows(profile).Bypass);
    }

    [Fact]
    public void WindowsSettingsMakeAProfile()
    {
        var windows = new ProxySettings(true, true, " http://pac.example.com/proxy.pac ", true, "proxy.example.com:8080", "*.example.com; 10.* ;<local>;");

        Assert.Equal(
            new ProxyProfile
            {
                AutoDetect = true,
                UseScript = true,
                ScriptAddress = "http://pac.example.com/proxy.pac",
                UseServer = true,
                Server = "proxy.example.com:8080",
                Exceptions = ["*.example.com", "10.*"],
                BypassLocal = true,
            },
            ProxyMatching.FromWindows(windows));
    }

    [Fact]
    public void AProfileMatchesTheSettingsItWrites()
    {
        Assert.True(ProxyMatching.Matches(Office, ProxyMatching.ToWindows(Office)));
    }

    [Theory]
    [InlineData("PROXY.example.com:8080", "10.*;*.EXAMPLE.com;<local>")]
    [InlineData(" proxy.example.com:8080 ", " *.example.com ; 10.* ; <local> ")]
    [InlineData("http://proxy.example.com:8080", "*.example.com;10.*;<local>")]
    public void CaseSpacesHttpAndTheOrderOfExceptionsDontCount(string server, string bypass)
    {
        Assert.True(ProxyMatching.Matches(Office, new ProxySettings(false, false, "", true, server, bypass)));
    }

    [Fact]
    public void AServerForEachProtocolMatchesWithHttpBeforeEachServer()
    {
        var profile = new ProxyProfile { UseServer = true, Server = "http=a.test:80;https=b.test:443" };

        Assert.True(ProxyMatching.Matches(profile, new ProxySettings(false, false, "", true, "http=http://a.test:80;https=b.test:443", "")));
        Assert.False(ProxyMatching.Matches(profile, new ProxySettings(false, false, "", true, "http=a.test:80", "")));
    }

    [Theory]
    [InlineData(true, false, true, "proxy.example.com:8080", "*.example.com;10.*;<local>")]
    [InlineData(false, false, false, "proxy.example.com:8080", "*.example.com;10.*;<local>")]
    [InlineData(false, false, true, "other.example.com:8080", "*.example.com;10.*;<local>")]
    [InlineData(false, false, true, "proxy.example.com:8080", "*.example.com;10.*")]
    [InlineData(false, false, true, "proxy.example.com:8080", "*.example.com;<local>")]
    [InlineData(false, true, true, "proxy.example.com:8080", "*.example.com;10.*;<local>")]
    public void AnyDifferenceInThePartsThatAreOnDoesntMatch(bool autoDetect, bool useScript, bool useServer, string server, string bypass)
    {
        Assert.False(ProxyMatching.Matches(Office, new ProxySettings(autoDetect, useScript, "", useServer, server, bypass)));
    }

    [Fact]
    public void TheValuesOfPartsThatAreOffDontCount()
    {
        var detect = new ProxyProfile { AutoDetect = true, Server = "old.example.com:80", ScriptAddress = "http://old.example.com/proxy.pac" };

        Assert.True(ProxyMatching.Matches(detect, new ProxySettings(true, false, "http://other.example.com/proxy.pac", false, "other.example.com:80", "<local>")));
    }

    [Fact]
    public void ScriptAddressesMatchIgnoringCase()
    {
        var script = new ProxyProfile { UseScript = true, ScriptAddress = "http://pac.example.com/proxy.pac" };

        Assert.True(ProxyMatching.Matches(script, new ProxySettings(false, true, "HTTP://PAC.example.com/proxy.pac", false, "", "")));
        Assert.False(ProxyMatching.Matches(script, new ProxySettings(false, true, "http://pac.example.com/other.pac", false, "", "")));
    }
}
