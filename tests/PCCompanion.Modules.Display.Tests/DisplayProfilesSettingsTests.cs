using System.Linq;
using System.Text.Json;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Display.Tests;

public sealed class DisplayProfilesSettingsTests
{
    private const string Example = """
        {
          "enabled": true,
          "profiles": [
            {
              "name": "Desk",
              "displays": [
                {
                  "model": "DELA0A3",
                  "serial": "5f1c0e9a2b7d4c61",
                  "connection": "\\\\?\\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
                  "builtIn": false,
                  "name": "DELL U2720Q",
                  "on": true,
                  "main": true,
                  "x": 0,
                  "y": 0,
                  "width": 3840,
                  "height": 2160,
                  "refreshRate": { "numerator": 60000, "denominator": 1001 },
                  "orientation": "Landscape",
                  "hdr": false
                },
                {
                  "model": "BOE0A1E",
                  "serial": "",
                  "connection": "\\\\?\\DISPLAY#BOE0A1E#4&3a2b1c0d&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
                  "builtIn": true,
                  "name": "",
                  "on": true,
                  "main": false,
                  "x": 3840,
                  "y": 960,
                  "width": 1920,
                  "height": 1200,
                  "refreshRate": { "numerator": 120000, "denominator": 1000 },
                  "orientation": "Landscape",
                  "hdr": null
                }
              ]
            },
            {
              "name": "TV",
              "displays": [
                {
                  "model": "SAM7088",
                  "serial": "",
                  "connection": "\\\\?\\DISPLAY#SAM7088#5&1f2e3d4c&0&UID4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
                  "builtIn": false,
                  "name": "SAMSUNG",
                  "on": true,
                  "main": true,
                  "x": 0,
                  "y": 0,
                  "width": 3840,
                  "height": 2160,
                  "refreshRate": { "numerator": 120000, "denominator": 1000 },
                  "orientation": "Landscape",
                  "hdr": true
                },
                {
                  "model": "DELA0A3",
                  "serial": "5f1c0e9a2b7d4c61",
                  "connection": "\\\\?\\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
                  "builtIn": false,
                  "name": "DELL U2720Q",
                  "on": false
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithNoProfiles()
    {
        var settings = DisplayProfilesSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Profiles);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = DisplayProfilesSettings.Read(Section(Example));

        Assert.True(settings.Enabled);
        Assert.Equal(["Desk", "TV"], settings.Profiles.Select(profile => profile.Name));
        var desk = settings.Profiles[0].Displays;
        Assert.Equal(
            new ProfileDisplay
            {
                Model = "DELA0A3",
                Serial = "5f1c0e9a2b7d4c61",
                Connection = @"\\?\DISPLAY#DELA0A3#5&1f2e3d4c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
                Name = "DELL U2720Q",
                On = true,
                Main = true,
                Width = 3840,
                Height = 2160,
                RefreshRate = new ProfileRefreshRate { Numerator = 60000, Denominator = 1001 },
                Orientation = DisplayOrientation.Landscape,
                Hdr = false,
            },
            desk[0]);
        Assert.True(desk[1].BuiltIn);
        Assert.Null(desk[1].Hdr);
        Assert.Equal(new System.Drawing.Rectangle(3840, 960, 1920, 1200), desk[1].Area());
        Assert.False(settings.Profiles[1].Displays[1].On);
    }

    [Fact]
    public void WrittenSettingsReadBackTheSame()
    {
        var settings = DisplayProfilesSettings.Read(Section(Example));

        Assert.Equal(settings, DisplayProfilesSettings.Read(settings.Write()));
    }

    [Fact]
    public void ADisplayThatsOffIsWrittenWithOnlyWhatRecognizesAndNamesIt()
    {
        var written = DisplayProfilesSettings.Read(Section(Example)).Write();
        var off = written.GetProperty("profiles")[1].GetProperty("displays")[1];

        Assert.Equal(
            ["model", "serial", "connection", "builtIn", "name", "on"],
            off.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void ADisplayWithoutHdrThatsOnIsWrittenWithHdrNull()
    {
        var written = DisplayProfilesSettings.Read(Section(Example)).Write();
        var builtIn = written.GetProperty("profiles")[0].GetProperty("displays")[1];

        Assert.Equal(JsonValueKind.Null, builtIn.GetProperty("hdr").ValueKind);
        Assert.Equal(960, builtIn.GetProperty("y").GetInt32());
    }

    [Fact]
    public void TurningItOffKeepsTheProfiles()
    {
        var settings = DisplayProfilesSettings.Read(Section(Example)).WithEnabled(false);

        Assert.False(settings.Enabled);
        Assert.Equal(2, settings.Profiles.Count);
    }

    [Fact]
    public void ADisplayTurnedOnItsSideHasItsResolutionTurnedOnTheDesktop()
    {
        var display = new ProfileDisplay { X = -1080, Y = 0, Width = 1920, Height = 1080, Orientation = DisplayOrientation.Portrait };

        Assert.Equal(new System.Drawing.Rectangle(-1080, 0, 1080, 1920), display.Area());
    }

    [Theory]
    [InlineData("""{ "profiles": [{ "name": "Desk", "displays": [{ "orientation": "Sideways" }] }] }""")]
    [InlineData("""{ "profiles": [{ "name": null }] }""")]
    [InlineData("""{ "profiles": [{ "name": "Desk", "displays": [{ "width": "wide" }] }] }""")]
    public void ASectionThatDoesntMatchTheFormatIsRefused(string json)
    {
        Assert.Throws<JsonException>(() => DisplayProfilesSettings.Read(Section(json)));
    }
}
