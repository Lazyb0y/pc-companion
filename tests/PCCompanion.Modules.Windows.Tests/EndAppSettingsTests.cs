using System.Text.Json;
using PCCompanion.Modules.Windows.EndApp;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class EndAppSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithCtrlAltEsc()
    {
        Assert.Equal(new EndAppSettings { Enabled = false, Shortcut = EndAppShortcut.ControlAltEscape }, EndAppSettings.Read(null));
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = EndAppSettings.Read(Section("""
            {
              "enabled": true,
              "shortcut": "ControlAltEscape"
            }
            """));

        Assert.Equal(new EndAppSettings { Enabled = true }, settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new EndAppSettings { Enabled = true }, EndAppSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Theory]
    [InlineData(EndAppShortcut.None)]
    [InlineData(EndAppShortcut.WindowsControlEscape)]
    public void WriteThenReadReturnsTheSameSettings(EndAppShortcut shortcut)
    {
        var settings = new EndAppSettings { Enabled = true, Shortcut = shortcut };

        Assert.Equal(settings, EndAppSettings.Read(settings.Write()));
    }

    [Fact]
    public void AnUnknownShortcutNumberIsKeptAsItIs()
    {
        Assert.Equal((EndAppShortcut)7, EndAppSettings.Read(Section("""{ "shortcut": 7 }""")).Shortcut);
    }

    [Fact]
    public void ASectionThatDoesNotMatchTheFormatCantBeRead()
    {
        Assert.Throws<JsonException>(() => EndAppSettings.Read(Section("""{ "enabled": "yes" }""")));
    }

    [Fact]
    public void WithEnabledKeepsTheOtherSettings()
    {
        var settings = new EndAppSettings { Shortcut = EndAppShortcut.None };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
