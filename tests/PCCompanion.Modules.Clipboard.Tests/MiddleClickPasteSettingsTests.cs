using System.Text.Json;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Clipboard.Tests;

public sealed class MiddleClickPasteSettingsTests
{
    private static readonly ListedApp Paint = new() { Name = "Example Paint", Program = @"C:\Program Files\Example\paint.exe" };

    [Fact]
    public void WithoutASectionItsOffWithNoAppsListed()
    {
        var settings = MiddleClickPasteSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.ExcludedApps);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = MiddleClickPasteSettings.Read(Section("""
            {
              "enabled": true,
              "excludedApps": [
                { "name": "Example Paint", "program": "C:\\Program Files\\Example\\paint.exe" }
              ]
            }
            """));

        Assert.Equal(new MiddleClickPasteSettings { Enabled = true, ExcludedApps = [Paint] }, settings);
    }

    [Fact]
    public void WritingAndReadingGivesTheSameSettings()
    {
        var settings = new MiddleClickPasteSettings { Enabled = true, ExcludedApps = [Paint, new ListedApp { Name = "Browser", AppId = "Browser.Profile1" }] };

        Assert.Equal(settings, MiddleClickPasteSettings.Read(settings.Write()));
    }

    [Fact]
    public void AppsWithoutAnIdOrWithBothOrListedTwiceAreLeftOut()
    {
        var settings = MiddleClickPasteSettings.Read(Section("""
            {
              "enabled": true,
              "excludedApps": [
                { "name": "Example Paint", "program": "C:\\Program Files\\Example\\paint.exe" },
                { "name": "Again", "program": "c:\\program files\\example\\PAINT.exe" },
                { "name": "Neither", "program": "  " },
                { "name": "Both", "appId": "Some.App", "program": "C:\\Some\\app.exe" },
                null
              ]
            }
            """));

        Assert.Equal([Paint], settings.ExcludedApps);
    }

    [Fact]
    public void ASectionThatDoesntMatchTheFormatIsRefused() =>
        Assert.Throws<JsonException>(() => MiddleClickPasteSettings.Read(Section("""{ "enabled": "yes" }""")));

    [Fact]
    public void AppsAreAddedOnceAndRemovedByPlace()
    {
        var settings = new MiddleClickPasteSettings().WithExcludedApp(Paint);

        Assert.True(settings.Lists(Paint));
        Assert.Same(settings, settings.WithExcludedApp(Paint with { Name = "Paint again" }));
        Assert.Empty(settings.WithoutExcludedApp(0).ExcludedApps);
    }

    [Fact]
    public void TurningItOnKeepsTheList() =>
        Assert.Equal([Paint], new MiddleClickPasteSettings { ExcludedApps = [Paint] }.WithEnabled(true).ExcludedApps);
}
