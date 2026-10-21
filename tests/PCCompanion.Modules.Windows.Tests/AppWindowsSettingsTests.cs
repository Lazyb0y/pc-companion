using System.Text.Json;
using PCCompanion.Modules.Windows.AppWindows;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class AppWindowsSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffWithAltAndWithoutMinimizedWindows()
    {
        Assert.Equal(new AppWindowsSettings { Enabled = false, Shortcut = AppWindowsShortcut.AltAboveTab, IncludeMinimized = false }, AppWindowsSettings.Read(null));
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = AppWindowsSettings.Read(Section("""
            {
              "enabled": true,
              "shortcut": "AltAboveTab",
              "includeMinimized": false
            }
            """));

        Assert.Equal(new AppWindowsSettings { Enabled = true }, settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(new AppWindowsSettings { Enabled = true }, AppWindowsSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new AppWindowsSettings { Enabled = true, Shortcut = AppWindowsShortcut.WindowsAboveTab, IncludeMinimized = true };

        var written = settings.Write();

        Assert.Equal("WindowsAboveTab", written.GetProperty("shortcut").GetString());
        Assert.Equal(settings, AppWindowsSettings.Read(written));
    }

    [Fact]
    public void AnUnknownShortcutNameCanNotBeRead()
    {
        Assert.Throws<JsonException>(() => AppWindowsSettings.Read(Section("""{ "enabled": true, "shortcut": "CtrlAboveTab" }""")));
    }

    [Fact]
    public void AShortcutNumberReadsAsAnyValue()
    {
        Assert.Equal((AppWindowsShortcut)7, AppWindowsSettings.Read(Section("""{ "shortcut": 7 }""")).Shortcut);
    }
}
