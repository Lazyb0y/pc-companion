using System.Text.Json;
using PCCompanion.Modules.Windows.MinimizeToTray;
using PCCompanion.Platform.Windows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class MinimizeToTraySettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithTheShortcutShiftMinimizeAndNoApps()
    {
        var settings = MinimizeToTraySettings.Read(null);

        Assert.Equal(new MinimizeToTraySettings { Enabled = false, Shortcut = MinimizeToTrayShortcut.WindowsShiftZ, ShiftMinimize = true, MinimizedApps = [] }, settings);
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = MinimizeToTraySettings.Read(Section("""
            {
              "enabled": true,
              "shortcut": "WindowsShiftZ",
              "shiftMinimize": true,
              "minimizedApps": [
                { "name": "Outlook", "program": "C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE" },
                { "name": "Photos", "appId": "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" }
              ]
            }
            """));

        Assert.Equal(
            new MinimizeToTraySettings
            {
                Enabled = true,
                MinimizedApps =
                [
                    new ListedApp { Name = "Outlook", Program = @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE" },
                    new ListedApp { Name = "Photos", AppId = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App" },
                ],
            },
            settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new MinimizeToTraySettings { Enabled = true }, MinimizeToTraySettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new MinimizeToTraySettings
        {
            Enabled = true,
            Shortcut = MinimizeToTrayShortcut.None,
            ShiftMinimize = false,
            MinimizedApps = [new ListedApp { Name = "App", Program = @"C:\Apps\App.exe" }],
        };

        Assert.Equal(settings, MinimizeToTraySettings.Read(settings.Write()));
    }

    [Fact]
    public void ListedAppsThatDontMatchTheFormatAreLeftOut()
    {
        var settings = MinimizeToTraySettings.Read(Section("""
            {
              "minimizedApps": [
                null,
                { "name": "Blank", "program": "  " },
                { "name": "Both", "program": "C:\\a.exe", "appId": "A" },
                { "name": "Neither" },
                { "name": "Kept", "appId": "B", "program": "" }
              ]
            }
            """));

        Assert.Equal([new ListedApp { Name = "Kept", AppId = "B" }], settings.MinimizedApps);
    }

    [Fact]
    public void AnUnknownShortcutNumberIsKeptAsItIs()
    {
        Assert.Equal((MinimizeToTrayShortcut)7, MinimizeToTraySettings.Read(Section("""{ "shortcut": 7 }""")).Shortcut);
    }

    [Fact]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead()
    {
        Assert.Throws<JsonException>(() => MinimizeToTraySettings.Read(Section("""{ "shiftMinimize": "yes" }""")));
    }

    [Fact]
    public void AnAppIsListedOnce()
    {
        var app = new ListedApp { Name = "App", Program = @"C:\Apps\App.exe" };
        var settings = new MinimizeToTraySettings().WithMinimizedApp(app);

        Assert.Same(settings, settings.WithMinimizedApp(app with { Program = @"c:\apps\app.exe" }));
        Assert.Same(settings, settings.WithMinimizedApp(new ListedApp { Name = "Neither" }));
        Assert.Empty(settings.WithoutMinimizedApp(0).MinimizedApps);
    }

    [Fact]
    public void WithEnabledKeepsTheOtherSettings()
    {
        var settings = new MinimizeToTraySettings { Shortcut = MinimizeToTrayShortcut.None, ShiftMinimize = false };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
