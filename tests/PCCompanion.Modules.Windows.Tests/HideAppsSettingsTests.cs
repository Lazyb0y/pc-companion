using System.Text.Json;
using PCCompanion.Modules.Windows.HideApps;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class HideAppsSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithBothShortcutsOnEveryDisplayAndWholeApps()
    {
        var settings = HideAppsSettings.Read(null);

        Assert.Equal(
            new HideAppsSettings
            {
                Enabled = false,
                HideAppShortcut = HideAppShortcut.WindowsControlH,
                HideOthersShortcut = HideOthersShortcut.WindowsControlShiftH,
                FrontDisplayOnly = false,
                BringBackWholeApp = true,
            },
            settings);
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = HideAppsSettings.Read(Section("""
            {
              "enabled": true,
              "hideAppShortcut": "WindowsControlH",
              "hideOthersShortcut": "WindowsControlShiftH",
              "frontDisplayOnly": false,
              "bringBackWholeApp": true
            }
            """));

        Assert.Equal(new HideAppsSettings { Enabled = true }, settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new HideAppsSettings { Enabled = true }, HideAppsSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new HideAppsSettings
        {
            Enabled = true,
            HideAppShortcut = HideAppShortcut.None,
            HideOthersShortcut = HideOthersShortcut.None,
            FrontDisplayOnly = true,
            BringBackWholeApp = false,
        };

        Assert.Equal(settings, HideAppsSettings.Read(settings.Write()));
    }

    [Fact]
    public void AnUnknownShortcutNumberIsKeptAsItIs()
    {
        var settings = HideAppsSettings.Read(Section("""{ "hideAppShortcut": 7, "hideOthersShortcut": 8 }"""));

        Assert.Equal((HideAppShortcut)7, settings.HideAppShortcut);
        Assert.Equal((HideOthersShortcut)8, settings.HideOthersShortcut);
    }

    [Fact]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead()
    {
        Assert.Throws<JsonException>(() => HideAppsSettings.Read(Section("""{ "frontDisplayOnly": "yes" }""")));
    }

    [Fact]
    public void WithEnabledKeepsTheOtherSettings()
    {
        var settings = new HideAppsSettings { HideAppShortcut = HideAppShortcut.None, FrontDisplayOnly = true, BringBackWholeApp = false };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
