using System.Text.Json;
using PCCompanion.Modules.Windows.WindowTabs;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowTabsSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithDroppingAndTheWindowsControlShortcuts()
    {
        Assert.Equal(
            new WindowTabsSettings { Enabled = false, GroupByDropping = true, SwitchShortcuts = TabSwitchShortcuts.WindowsControlPage },
            WindowTabsSettings.Read(null));
    }

    [Fact]
    public void ReadReadsTheSpecsValues()
    {
        var settings = WindowTabsSettings.Read(Section("""
            {
              "enabled": true,
              "groupByDropping": true,
              "switchShortcuts": "WindowsControlPage"
            }
            """));

        Assert.Equal(new WindowTabsSettings { Enabled = true }, settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new WindowTabsSettings { Enabled = true }, WindowTabsSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new WindowTabsSettings { Enabled = true, GroupByDropping = false, SwitchShortcuts = TabSwitchShortcuts.WindowsAltPage };

        Assert.Equal(settings, WindowTabsSettings.Read(settings.Write()));
    }

    [Fact]
    public void AnUnknownShortcutNumberIsKeptAsItIs()
    {
        Assert.Equal((TabSwitchShortcuts)9, WindowTabsSettings.Read(Section("""{ "switchShortcuts": 9 }""")).SwitchShortcuts);
    }

    [Fact]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead()
    {
        Assert.Throws<JsonException>(() => WindowTabsSettings.Read(Section("""{ "groupByDropping": "yes" }""")));
    }

    [Fact]
    public void WithEnabledKeepsTheOtherSettings()
    {
        var settings = new WindowTabsSettings { GroupByDropping = false, SwitchShortcuts = TabSwitchShortcuts.None };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
