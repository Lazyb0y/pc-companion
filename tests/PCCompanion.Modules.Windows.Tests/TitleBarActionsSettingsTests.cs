using System.Text.Json;
using PCCompanion.Modules.Windows.TitleBarActions;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class TitleBarActionsSettingsTests
{
    [Fact]
    public void WithoutASectionMiddleClickMinimizesAndTheRestDoNothing()
    {
        var settings = TitleBarActionsSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Equal(TitleBarClickAction.Minimize, settings.MiddleClick);
        Assert.Equal(TitleBarClickAction.None, settings.ShiftMiddleClick);
        Assert.Equal(TitleBarClickAction.None, settings.ControlMiddleClick);
        Assert.Equal(TitleBarWheelAction.None, settings.Wheel);
        Assert.Equal(TitleBarWheelAction.None, settings.ControlWheel);
    }

    [Fact]
    public void TheSpecsExampleIsRead()
    {
        var settings = TitleBarActionsSettings.Read(Section("""
            {
              "enabled": true,
              "middleClick": "Minimize",
              "shiftMiddleClick": "MinimizeToTray",
              "controlMiddleClick": "SendToBack",
              "wheel": "None",
              "controlWheel": "Opacity"
            }
            """));

        Assert.Equal(
            new TitleBarActionsSettings
            {
                Enabled = true,
                MiddleClick = TitleBarClickAction.Minimize,
                ShiftMiddleClick = TitleBarClickAction.MinimizeToTray,
                ControlMiddleClick = TitleBarClickAction.SendToBack,
                Wheel = TitleBarWheelAction.None,
                ControlWheel = TitleBarWheelAction.Opacity,
            },
            settings);
    }

    [Fact]
    public void WritingAndReadingGivesTheSameSettings()
    {
        var settings = new TitleBarActionsSettings { Enabled = true, MiddleClick = TitleBarClickAction.HideApp, Wheel = TitleBarWheelAction.Opacity };

        Assert.Equal(settings, TitleBarActionsSettings.Read(settings.Write()));
        Assert.Equal("HideApp", settings.Write().GetProperty("middleClick").GetString());
    }

    [Fact]
    public void AnUnknownNameCantBeRead() =>
        Assert.ThrowsAny<JsonException>(() => TitleBarActionsSettings.Read(Section("""{ "middleClick": "Close" }""")));

    [Fact]
    public void WithEnabledChangesOnlyEnabled()
    {
        var settings = new TitleBarActionsSettings { ControlMiddleClick = TitleBarClickAction.SendToBack };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
