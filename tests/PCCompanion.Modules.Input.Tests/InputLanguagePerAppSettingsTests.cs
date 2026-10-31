using System.Linq;
using System.Text.Json;
using PCCompanion.Modules.Input.InputLanguagePerApp;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

public sealed class InputLanguagePerAppSettingsTests
{
    private static readonly InputMethodId German = new(0x0407, 0x00000407);
    private static readonly InputMethodId UnitedStates = new(0x0409, 0x00000409);

    [Fact]
    public void WithoutASectionTheFeatureIsOffAndRemembersEachAppsLanguageWithANotice()
    {
        var settings = InputLanguagePerAppSettings.Read(null);

        Assert.Equal(
            new InputLanguagePerAppSettings { Enabled = false, Apps = [], OtherApps = OtherApps.Remember, OtherAppsInputMethod = null, ShowNotice = true },
            settings);
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = InputLanguagePerAppSettings.Read(Section("""
            {
              "enabled": true,
              "apps": [
                { "name": "Telegram", "program": "C:\\Users\\Me\\AppData\\Roaming\\Telegram Desktop\\Telegram.exe", "inputMethod": "0407:00000407" },
                { "name": "Terminal", "appId": "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "inputMethod": "0409:00000409" }
              ],
              "otherApps": "Remember",
              "otherAppsInputMethod": null,
              "showNotice": true
            }
            """));

        Assert.Equal(
            new InputLanguagePerAppSettings
            {
                Enabled = true,
                Apps =
                [
                    new AppLanguage { Name = "Telegram", Program = @"C:\Users\Me\AppData\Roaming\Telegram Desktop\Telegram.exe", InputMethod = "0407:00000407" },
                    new AppLanguage { Name = "Terminal", AppId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", InputMethod = "0409:00000409" },
                ],
            },
            settings);
        Assert.True(settings.Apps[0].TryGetInputMethod(out var inputMethod));
        Assert.Equal(German, inputMethod);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new InputLanguagePerAppSettings { Enabled = true }, InputLanguagePerAppSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new InputLanguagePerAppSettings
        {
            Enabled = true,
            Apps = [new AppLanguage { Name = "App", Program = @"C:\Apps\App.exe", InputMethod = "0411" }],
            OtherApps = OtherApps.Use,
            OtherAppsInputMethod = "0409:00020409",
            ShowNotice = false,
        };

        Assert.Equal(settings, InputLanguagePerAppSettings.Read(settings.Write()));
    }

    [Fact]
    public void ListedAppsThatDontMatchTheFormatOrAreListedTwiceAreLeftOut()
    {
        var settings = InputLanguagePerAppSettings.Read(Section("""
            {
              "apps": [
                null,
                { "name": "Blank", "program": "  ", "inputMethod": "0407:00000407" },
                { "name": "Both", "program": "C:\\a.exe", "appId": "A", "inputMethod": "0407:00000407" },
                { "name": "Neither", "inputMethod": "0407:00000407" },
                { "name": "Kept", "appId": "B", "program": "", "inputMethod": "0407:00000407" },
                { "name": "Again", "appId": "b", "inputMethod": "0409:00000409" }
              ]
            }
            """));

        Assert.Equal([new AppLanguage { Name = "Kept", AppId = "B", InputMethod = "0407:00000407" }], settings.Apps);
    }

    [Fact]
    public void IdentifiersAreWrittenAsWindowsWritesThemAndOnesThatDontParseAreKept()
    {
        var settings = InputLanguagePerAppSettings.Read(Section("""
            {
              "apps": [
                { "name": "Lower", "appId": "A", "inputMethod": "0409:0002040a" },
                { "name": "Unknown", "appId": "B", "inputMethod": "German" },
                { "name": "None", "appId": "C" }
              ],
              "otherApps": "Use",
              "otherAppsInputMethod": "0411:{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}"
            }
            """));

        Assert.Equal(["0409:0002040A", "German", null], settings.Apps.Select(rule => rule.InputMethod));
        Assert.True(settings.Apps[0].TryGetInputMethod(out _));
        Assert.False(settings.Apps[1].TryGetInputMethod(out _));
        Assert.False(settings.Apps[2].TryGetInputMethod(out _));
        Assert.False(settings.TryGetOtherAppsInputMethod(out _));
    }

    [Fact]
    public void TheOneLanguageForOtherAppsIsReadWithItsIdentifier()
    {
        var settings = InputLanguagePerAppSettings.Read(Section("""{ "otherApps": "Use", "otherAppsInputMethod": "0409:00000409" }"""));

        Assert.Equal(OtherApps.Use, settings.OtherApps);
        Assert.True(settings.TryGetOtherAppsInputMethod(out var inputMethod));
        Assert.Equal(UnitedStates, inputMethod);
        Assert.False(new InputLanguagePerAppSettings().TryGetOtherAppsInputMethod(out _));
    }

    [Fact]
    public void AnUnknownBehaviorNumberForOtherAppsIsRemember()
    {
        Assert.Equal(OtherApps.Remember, InputLanguagePerAppSettings.Read(Section("""{ "otherApps": 7 }""")).OtherApps);
    }

    [Theory]
    [InlineData("""{ "showNotice": "yes" }""")]
    [InlineData("""{ "otherApps": "Sometimes" }""")]
    [InlineData("""{ "apps": { "name": "App" } }""")]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead(string json)
    {
        Assert.Throws<JsonException>(() => InputLanguagePerAppSettings.Read(Section(json)));
    }

    [Fact]
    public void AnAppIsListedOnceWithItsInputMethod()
    {
        var app = new ListedApp { Name = "App", Program = @"C:\Apps\App.exe" };
        var settings = new InputLanguagePerAppSettings().WithApp(app, German);

        Assert.Equal([new AppLanguage { Name = "App", Program = @"C:\Apps\App.exe", InputMethod = "0407:00000407" }], settings.Apps);
        Assert.True(settings.Lists(app with { Program = @"c:\apps\app.exe" }));
        Assert.Same(settings, settings.WithApp(app with { Program = @"c:\apps\app.exe" }, UnitedStates));
        Assert.Same(settings, settings.WithApp(new ListedApp { Name = "Neither" }, UnitedStates));
        Assert.Equal("0409:00000409", settings.WithInputMethodAt(0, UnitedStates).Apps[0].InputMethod);
        Assert.Empty(settings.WithoutApp(0).Apps);
    }

    [Fact]
    public void WithEnabledKeepsTheOtherSettings()
    {
        var settings = new InputLanguagePerAppSettings { OtherApps = OtherApps.Keep, ShowNotice = false };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
