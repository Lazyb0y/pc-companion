using PCCompanion.Modules.Network.NetworkProfiles;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Network.Tests;

public sealed class NetworkProfilesSettingsTests
{
    private const string Example = """
        {
          "enabled": true,
          "profiles": [
            {
              "name": "Office",
              "proxy": {
                "autoDetect": false,
                "useScript": false,
                "scriptAddress": "",
                "useServer": true,
                "server": "proxy.example.com:8080",
                "exceptions": ["*.example.com", "10.*"],
                "bypassLocal": true
              }
            },
            { "name": "Home", "proxy": { "autoDetect": true } }
          ]
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoProfiles()
    {
        var settings = NetworkProfilesSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Profiles);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = NetworkProfilesSettings.Read(Section(Example));

        Assert.True(settings.Enabled);
        Assert.Equal(2, settings.Profiles.Count);
        Assert.Equal("Office", settings.Profiles[0].Name);
        Assert.Equal(
            new ProxyProfile { UseServer = true, Server = "proxy.example.com:8080", Exceptions = ["*.example.com", "10.*"], BypassLocal = true },
            settings.Profiles[0].Proxy);
        Assert.Equal(new ProxyProfile { AutoDetect = true }, settings.Profiles[1].Proxy);
    }

    [Fact]
    public void WrittenSettingsReadBackTheSame()
    {
        var settings = NetworkProfilesSettings.Read(Section(Example));

        Assert.Equal(settings, NetworkProfilesSettings.Read(settings.Write()));
    }

    [Fact]
    public void TurningItOffKeepsTheProfiles()
    {
        var settings = NetworkProfilesSettings.Read(Section(Example)).WithEnabled(false);

        Assert.False(settings.Enabled);
        Assert.Equal(2, settings.Profiles.Count);
    }
}
