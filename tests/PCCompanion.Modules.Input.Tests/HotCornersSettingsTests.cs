using System.Linq;
using System.Text.Json;
using PCCompanion.Modules.Input.HotCorners;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class HotCornersSettingsTests
{
    [Fact]
    public void WithoutASectionTheFeatureIsOffAndTheTopLeftCornerShowsTaskView()
    {
        var settings = HotCornersSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.Equal(new CornerSettings { Action = HotCornerAction.TaskView }, settings.TopLeft);
        Assert.Equal(new CornerSettings(), settings.TopRight);
        Assert.Equal(new CornerSettings(), settings.BottomLeft);
        Assert.Equal(new CornerSettings(), settings.BottomRight);
        Assert.Equal(150, settings.ActivationDelayMilliseconds);
        Assert.True(settings.IgnoreFullScreenApps);
    }

    [Fact]
    public void ReadReadsTheExampleInTheSpec()
    {
        var settings = HotCornersSettings.Read(Section("""
            {
              "enabled": true,
              "topLeft": { "action": "TaskView", "key": "None" },
              "topRight": { "action": "None", "key": "None" },
              "bottomLeft": { "action": "None", "key": "None" },
              "bottomRight": { "action": "None", "key": "Control" },
              "activationDelayMilliseconds": 150,
              "ignoreFullScreenApps": true
            }
            """));

        Assert.Equal(HotCornersSettings.Read(null) with { Enabled = true, BottomRight = new CornerSettings { Key = HotCornerKey.Control } }, settings);
    }

    [Fact]
    public void ReadUsesTheDefaultsForMissingValues()
    {
        Assert.Equal(HotCornersSettings.Read(null) with { Enabled = true }, HotCornersSettings.Read(Section("""{ "enabled": true }""")));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var settings = new HotCornersSettings
        {
            Enabled = true,
            TopLeft = new CornerSettings(),
            TopRight = new CornerSettings { Action = HotCornerAction.Lock, Key = HotCornerKey.Windows },
            BottomLeft = new CornerSettings { Action = HotCornerAction.ShowDesktop },
            BottomRight = new CornerSettings { Action = HotCornerAction.RunProgram, Key = HotCornerKey.Shift, Program = @"C:\Windows\System32\notepad.exe", Arguments = "" },
            ActivationDelayMilliseconds = 0,
            IgnoreFullScreenApps = false,
        };

        var section = settings.Write();

        Assert.Equal(settings, HotCornersSettings.Read(section));
        Assert.Equal("""{"action":"Lock","key":"Windows"}""", section.GetProperty("topRight").GetRawText());
    }

    [Fact]
    public void ACornerKeepsItsProgramAndArguments()
    {
        var settings = new HotCornersSettings
        {
            TopRight = new CornerSettings { Action = HotCornerAction.RunProgram, Program = @"C:\Program Files\App\App.exe", Arguments = "--new \"two words\"" },
            BottomLeft = new CornerSettings { Action = HotCornerAction.Lock, Program = @"C:\Tools\Old.exe" },
        };

        var section = settings.Write();

        Assert.Equal(settings, HotCornersSettings.Read(section));
        Assert.Equal("""{"action":"RunProgram","key":"None","program":"C:\\Program Files\\App\\App.exe","arguments":"--new \u0022two words\u0022"}""", section.GetProperty("topRight").GetRawText());
    }

    [Fact]
    public void ACornerWithoutAProgramWritesNone()
    {
        Assert.Equal("""{"action":"Lock","key":"None"}""", new HotCornersSettings { TopLeft = new CornerSettings { Action = HotCornerAction.Lock } }.Write().GetProperty("topLeft").GetRawText());
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(1000, 1000)]
    [InlineData(5000, 1000)]
    public void ReadKeepsTheActivationDelayInRange(int stored, int expected)
    {
        Assert.Equal(expected, HotCornersSettings.Read(Section($$"""{ "activationDelayMilliseconds": {{stored}} }""")).ActivationDelayMilliseconds);
    }

    [Theory]
    [InlineData("""{ "action": 99 }""")]
    [InlineData("""{ "action": "Lock", "key": 42 }""")]
    public void ACornerWithAnUnknownNumberDoesNothing(string corner)
    {
        Assert.Equal(new CornerSettings(), HotCornersSettings.Read(Section($$"""{ "bottomLeft": {{corner}} }""")).BottomLeft);
    }

    [Theory]
    [InlineData("""{ "topLeft": { "action": "OpenEverything" } }""")]
    [InlineData("""{ "topLeft": null }""")]
    [InlineData("""{ "enabled": "yes" }""")]
    public void ReadRejectsASectionThatDoesNotMatch(string json)
    {
        Assert.ThrowsAny<JsonException>(() => HotCornersSettings.Read(Section(json)));
    }

    [Fact]
    public void ForReturnsEachCorner()
    {
        var settings = new HotCornersSettings
        {
            TopLeft = new CornerSettings { Action = HotCornerAction.TaskView },
            TopRight = new CornerSettings { Action = HotCornerAction.Start },
            BottomLeft = new CornerSettings { Action = HotCornerAction.Search },
            BottomRight = new CornerSettings { Action = HotCornerAction.Notifications },
        };

        Assert.Equal(
            [HotCornerAction.TaskView, HotCornerAction.Start, HotCornerAction.Search, HotCornerAction.Notifications],
            [.. new[] { ScreenCorner.TopLeft, ScreenCorner.TopRight, ScreenCorner.BottomLeft, ScreenCorner.BottomRight }.Select(corner => settings.For(corner).Action)]);
    }

    private static JsonElement Section(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
