using System.Linq;
using System.Text.Json;
using PCCompanion.Modules.Desktop.DynamicWallpaper;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

public sealed class DynamicWallpaperSettingsTests
{
    // The example in docs/features/desktop/dynamic-wallpaper.md.
    private const string Example = """
        {
          "enabled": true,
          "changeWith": "Sun",
          "location": "Coordinates",
          "latitude": 23.8,
          "longitude": 90.4,
          "fit": "Fill",
          "eachDisplay": true,
          "displays": [
            {
              "model": "DELF11B",
              "serial": "5f1c0e9a2b7d4c61",
              "connection": "\\\\?\\DISPLAY#DELF11B#5&2a1f3e84&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}",
              "builtIn": false,
              "name": "DELL E2420H"
            }
          ],
          "sun": {
            "sunrise": null,
            "day": { "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg" },
            "sunset": { "picture": "C:\\Users\\Ana\\Pictures\\Wallpapers\\Dusk.jpg" },
            "night": {
              "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img19.jpg",
              "displays": [{ "folder": "C:\\Users\\Ana\\Pictures\\Night" }]
            }
          },
          "times": [
            { "at": "07:00", "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg" },
            { "at": "19:00", "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img19.jpg" }
          ],
          "windowsMode": {
            "light": { "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg" },
            "dark": { "picture": "C:\\Windows\\Web\\Wallpaper\\Windows\\img19.jpg" }
          }
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithItsDefaults()
    {
        var settings = DynamicWallpaperSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Equal(ChangeWith.Sun, settings.ChangeWith);
        Assert.Equal(LocationSource.TimeZone, settings.Location);
        Assert.Equal(WallpaperFit.Fill, settings.Fit);
        Assert.False(settings.EachDisplay);
        Assert.Empty(settings.Times);
    }

    [Fact]
    public void TheSpecsExampleReads()
    {
        var settings = DynamicWallpaperSettings.Read(Parse(Example));

        Assert.True(settings.Enabled);
        Assert.Equal(LocationSource.Coordinates, settings.Location);
        Assert.Equal(23.8, settings.Latitude);
        Assert.Equal(90.4, settings.Longitude);
        Assert.True(settings.PicturesForEachDisplay);
        Assert.Equal("DELL E2420H", Assert.Single(settings.Displays).Name);
        Assert.Null(settings.Sun.Sunrise);
        Assert.Equal(@"C:\Users\Ana\Pictures\Wallpapers\Dusk.jpg", settings.Sun.Sunset!.Picture);
        Assert.Equal(@"C:\Users\Ana\Pictures\Night", settings.Sun.Night.For(0).Folder);
        Assert.Equal(@"C:\Windows\Web\Wallpaper\Windows\img19.jpg", settings.Sun.Night.For(1).Picture);
        Assert.Equal(["07:00", "19:00"], settings.Times.Select(time => time.At));
        Assert.Equal(@"C:\Windows\Web\Wallpaper\Windows\img19.jpg", settings.WindowsMode.Dark.Picture);
    }

    [Fact]
    public void WritingAndReadingAgainKeepsTheSettings()
    {
        var settings = DynamicWallpaperSettings.Read(Parse(Example));

        Assert.Equal(settings, DynamicWallpaperSettings.Read(settings.Write()));
    }

    [Fact]
    public void OnlyWhatsSetIsWritten()
    {
        var written = DynamicWallpaperSettings.First(@"C:\Day.jpg", @"C:\Night.jpg", WallpaperFit.Fit).Write();

        Assert.False(written.TryGetProperty("latitude", out _));
        Assert.Equal(JsonValueKind.Null, written.GetProperty("sun").GetProperty("sunrise").ValueKind);
        var day = written.GetProperty("sun").GetProperty("day");
        Assert.Equal(@"C:\Day.jpg", day.GetProperty("picture").GetString());
        Assert.False(day.TryGetProperty("at", out _));
        Assert.False(day.TryGetProperty("folder", out _));
        Assert.False(day.TryGetProperty("displays", out _));
        Assert.Equal("Fit", written.GetProperty("fit").GetString());
    }

    [Fact]
    public void TheFirstSettingsUseTheDayAndNightPicturesEverywhere()
    {
        var settings = DynamicWallpaperSettings.First(@"C:\Day.jpg", @"C:\Night.jpg", WallpaperFit.Center);

        Assert.False(settings.Enabled);
        Assert.Equal(ChangeWith.Sun, settings.ChangeWith);
        Assert.Equal(LocationSource.TimeZone, settings.Location);
        Assert.Equal(WallpaperFit.Center, settings.Fit);
        Assert.False(settings.EachDisplay);
        Assert.Equal(@"C:\Day.jpg", settings.Sun.Day.Picture);
        Assert.Equal(@"C:\Night.jpg", settings.Sun.Night.Picture);
        Assert.Null(settings.Sun.Sunrise);
        Assert.Null(settings.Sun.Sunset);
        Assert.Equal([("07:00", @"C:\Day.jpg"), ("19:00", @"C:\Night.jpg")], settings.Times.Select(time => (time.At, time.Picture)));
        Assert.Equal(@"C:\Day.jpg", settings.WindowsMode.Light.Picture);
        Assert.Equal(@"C:\Night.jpg", settings.WindowsMode.Dark.Picture);
    }

    [Fact]
    public void SpanHasNoPictureForEachDisplay()
    {
        var settings = new DynamicWallpaperSettings { EachDisplay = true, Fit = WallpaperFit.Span };

        Assert.False(settings.PicturesForEachDisplay);
    }

    [Fact]
    public void ADisplayWithoutItsOwnPictureHasThePeriods()
    {
        var period = new WallpaperPeriod { Picture = @"C:\All.jpg", Displays = [null, new() { Picture = @"C:\Second.jpg" }] };

        Assert.Equal(@"C:\All.jpg", period.For(0).Picture);
        Assert.Equal(@"C:\Second.jpg", period.For(1).Picture);
        Assert.Equal(@"C:\All.jpg", period.For(2).Picture);
        Assert.Equal(@"C:\All.jpg", period.For(-1).Picture);
    }

    [Fact]
    public void UnknownChoicesReadAsTheDefaultsAndAreNamed()
    {
        var section = Parse("""{ "enabled": true, "changeWith": "Moon", "location": "Gps", "fit": "Zoom" }""");

        var settings = DynamicWallpaperSettings.Read(section);

        Assert.Equal(ChangeWith.Sun, settings.ChangeWith);
        Assert.Equal(LocationSource.TimeZone, settings.Location);
        Assert.Equal(WallpaperFit.Fill, settings.Fit);
        Assert.Equal(["changeWith", "location", "fit"], DynamicWallpaperSettings.UnknownChoices(section));
        Assert.Empty(DynamicWallpaperSettings.UnknownChoices(Parse(Example)));
    }

    [Fact]
    public void ASectionOfTheWrongShapeCantBeRead() =>
        Assert.ThrowsAny<JsonException>(() => DynamicWallpaperSettings.Read(Parse("""{ "times": 3 }""")));

    [Fact]
    public void TurningOnKeepsTheOtherSettings()
    {
        var settings = DynamicWallpaperSettings.Read(Parse(Example)).WithEnabled(false);

        Assert.False(settings.Enabled);
        Assert.Equal(WallpaperFit.Fill, settings.Fit);
        Assert.Equal(2, settings.Times.Count);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
