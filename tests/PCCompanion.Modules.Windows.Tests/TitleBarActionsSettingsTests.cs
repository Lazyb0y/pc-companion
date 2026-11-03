using System.Text.Json;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;
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
        Assert.Empty(settings.ExcludedApps);
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
              "controlWheel": "Opacity",
              "excludedApps": [
                { "name": "Firefox", "program": "C:\\Program Files\\Mozilla Firefox\\firefox.exe" }
              ]
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
                ExcludedApps = [new ListedApp { Name = "Firefox", Program = @"C:\Program Files\Mozilla Firefox\firefox.exe" }],
            },
            settings);
    }

    [Fact]
    public void WritingAndReadingGivesTheSameSettings()
    {
        var settings = new TitleBarActionsSettings
        {
            Enabled = true,
            MiddleClick = TitleBarClickAction.HideApp,
            Wheel = TitleBarWheelAction.Opacity,
            ExcludedApps = [new ListedApp { Name = "App", AppId = "App.Id" }],
        };

        Assert.Equal(settings, TitleBarActionsSettings.Read(settings.Write()));
        Assert.Equal("HideApp", settings.Write().GetProperty("middleClick").GetString());
    }

    [Fact]
    public void AnUnknownNameCantBeRead() =>
        Assert.ThrowsAny<JsonException>(() => TitleBarActionsSettings.Read(Section("""{ "middleClick": "Close" }""")));

    [Fact]
    public void ListedAppsThatDontMatchTheFormatOrAreListedAgainAreLeftOut()
    {
        var settings = TitleBarActionsSettings.Read(Section("""
            {
              "excludedApps": [
                null,
                { "name": "Blank", "program": "  " },
                { "name": "Both", "program": "C:\\a.exe", "appId": "A" },
                { "name": "Neither" },
                { "name": "Kept", "appId": "B", "program": "" },
                { "name": "Again", "appId": "b" }
              ]
            }
            """));

        Assert.Equal([new ListedApp { Name = "Kept", AppId = "B" }], settings.ExcludedApps);
    }

    [Fact]
    public void AnAppIsListedOnce()
    {
        var app = new ListedApp { Name = "App", Program = @"C:\Apps\App.exe" };
        var settings = new TitleBarActionsSettings().WithExcludedApp(app);

        Assert.Same(settings, settings.WithExcludedApp(app with { Program = @"c:\apps\app.exe" }));
        Assert.Same(settings, settings.WithExcludedApp(new ListedApp { Name = "Neither" }));
        Assert.Empty(settings.WithoutExcludedApp(0).ExcludedApps);
    }

    [Fact]
    public void WithEnabledChangesOnlyEnabled()
    {
        var settings = new TitleBarActionsSettings { ControlMiddleClick = TitleBarClickAction.SendToBack };

        Assert.Equal(settings with { Enabled = true }, settings.WithEnabled(true));
    }
}
