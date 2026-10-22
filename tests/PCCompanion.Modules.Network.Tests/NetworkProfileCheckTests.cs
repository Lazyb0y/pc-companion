using PCCompanion.Modules.Network.NetworkProfiles;
using Xunit;

namespace PCCompanion.Modules.Network.Tests;

public sealed class NetworkProfileCheckTests
{
    [Theory]
    [InlineData("Office", ProfileProblem.None)]
    [InlineData("  Office  ", ProfileProblem.None)]
    [InlineData("", ProfileProblem.NoName)]
    [InlineData("   ", ProfileProblem.NoName)]
    [InlineData("Office\nHome", ProfileProblem.InvalidName)]
    [InlineData("HOME", ProfileProblem.NameInUse)]
    [InlineData(" home ", ProfileProblem.NameInUse)]
    public void Names(string name, ProfileProblem expected)
    {
        Assert.Equal(expected, NetworkProfileCheck.CheckName(name, ["Home"]));
    }

    [Fact]
    public void NamesAreUpTo60Characters()
    {
        Assert.Equal(ProfileProblem.None, NetworkProfileCheck.CheckName(new string('a', 60), []));
        Assert.Equal(ProfileProblem.NameTooLong, NetworkProfileCheck.CheckName(new string('a', 61), []));
    }

    [Theory]
    [InlineData("proxy.example.com:8080")]
    [InlineData("  proxy.example.com:8080  ")]
    [InlineData("http://proxy.example.com:8080")]
    [InlineData("HTTPS://proxy.example.com:8443")]
    [InlineData("proxy")]
    [InlineData("10.0.0.1:3128")]
    [InlineData("[::1]:8080")]
    [InlineData("[fe80::1]")]
    [InlineData("http=proxy.example.com:8080;https=proxy.example.com:8443")]
    [InlineData("http=a.test:80;socks=b.test:1080;")]
    [InlineData("ftp=http://a.test:21")]
    public void ValidServers(string server)
    {
        Assert.Equal(ProfileProblem.None, NetworkProfileCheck.CheckServer(server));
    }

    [Theory]
    [InlineData("", ProfileProblem.NoServer)]
    [InlineData("  ", ProfileProblem.NoServer)]
    [InlineData("proxy.example.com:0", ProfileProblem.InvalidServer)]
    [InlineData("proxy.example.com:65536", ProfileProblem.InvalidServer)]
    [InlineData("proxy.example.com:80a", ProfileProblem.InvalidServer)]
    [InlineData("proxy.example.com:", ProfileProblem.InvalidServer)]
    [InlineData(":8080", ProfileProblem.InvalidServer)]
    [InlineData("proxy example.com:80", ProfileProblem.InvalidServer)]
    [InlineData("http=a.test:80;b.test:81", ProfileProblem.InvalidServer)]
    [InlineData("gopher=a.test:70", ProfileProblem.InvalidServer)]
    [InlineData("[::1:8080", ProfileProblem.InvalidServer)]
    [InlineData("[proxy]:8080", ProfileProblem.InvalidServer)]
    [InlineData("file://proxy:80", ProfileProblem.InvalidServer)]
    public void InvalidServers(string server, ProfileProblem expected)
    {
        Assert.Equal(expected, NetworkProfileCheck.CheckServer(server));
    }

    [Fact]
    public void ServersAreUpTo2048Characters()
    {
        Assert.Equal(ProfileProblem.TooLong, NetworkProfileCheck.CheckServer(new string('a', 2049)));
    }

    [Theory]
    [InlineData("http://pac.example.com/proxy.pac", ProfileProblem.None)]
    [InlineData("https://pac.example.com/proxy.pac", ProfileProblem.None)]
    [InlineData("", ProfileProblem.NoScriptAddress)]
    [InlineData("pac.example.com/proxy.pac", ProfileProblem.ScriptNotWebAddress)]
    [InlineData("file://C:/proxy.pac", ProfileProblem.ScriptNotWebAddress)]
    [InlineData("ftp://pac.example.com/proxy.pac", ProfileProblem.ScriptNotWebAddress)]
    public void ScriptAddresses(string address, ProfileProblem expected)
    {
        Assert.Equal(expected, NetworkProfileCheck.CheckScriptAddress(address));
    }

    [Fact]
    public void Exceptions()
    {
        Assert.Equal(ProfileProblem.None, NetworkProfileCheck.CheckExceptions([]));
        Assert.Equal(ProfileProblem.None, NetworkProfileCheck.CheckExceptions(["*.example.com", "10.*", "<local>"]));
        Assert.Equal(ProfileProblem.InvalidException, NetworkProfileCheck.CheckExceptions([""]));
        Assert.Equal(ProfileProblem.InvalidException, NetworkProfileCheck.CheckExceptions(["a b"]));
        Assert.Equal(ProfileProblem.InvalidException, NetworkProfileCheck.CheckExceptions(["a;b"]));
        Assert.Equal(ProfileProblem.TooLong, NetworkProfileCheck.CheckExceptions([new string('a', 1500), new string('b', 1500)]));
    }

    [Fact]
    public void OnlyThePartsThatAreOnAreChecked()
    {
        var off = new NetworkProfile { Name = "Home", Proxy = new ProxyProfile { Server = "not a server", Exceptions = [""], ScriptAddress = "not an address" } };
        var on = off with { Proxy = off.Proxy with { UseServer = true } };
        var script = off with { Proxy = off.Proxy with { UseScript = true } };

        Assert.Equal(ProfileProblem.None, NetworkProfileCheck.Check(off, []));
        Assert.Equal(ProfileProblem.InvalidServer, NetworkProfileCheck.Check(on, []));
        Assert.Equal(ProfileProblem.ScriptNotWebAddress, NetworkProfileCheck.Check(script, []));
    }
}
