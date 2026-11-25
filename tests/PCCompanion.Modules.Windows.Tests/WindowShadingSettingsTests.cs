using System;
using System.Text.Json;
using PCCompanion.Modules.Windows.WindowShading;
using Xunit;

namespace PCCompanion.Modules.Windows.Tests;

public sealed class WindowShadingSettingsTests
{
    [Fact]
    public void TheSpecsExampleReads()
    {
        var settings = Read("""{ "enabled": true, "keepOnTop": true, "hover": { "enabled": false, "rollDownAfterMs": 500, "rollUpAfterMs": 1000 } }""");

        Assert.True(settings.Enabled);
        Assert.True(settings.KeepOnTop);
        Assert.False(settings.Hover.Enabled);
        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.Hover.RollDownAfter);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.Hover.RollUpAfter);
    }

    [Fact]
    public void WithoutASectionItsOffAndKeepsOnTop()
    {
        var settings = WindowShadingSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.True(settings.KeepOnTop);
        Assert.False(settings.Hover.Enabled);
    }

    [Fact]
    public void ADelayThisVersionDoesntOfferIsKeptAndItsDefaultUsed()
    {
        var settings = Read("""{ "enabled": true, "hover": { "enabled": true, "rollDownAfterMs": 333, "rollUpAfterMs": 2000 } }""");

        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.Hover.RollDownAfter);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.Hover.RollUpAfter);
        Assert.Contains("\"rollDownAfterMs\":333", settings.Write().GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionThatCantBeReadThrows() =>
        Assert.ThrowsAny<JsonException>(() => Read("""{ "enabled": "yes" }"""));

    [Fact]
    public void TheHomeSwitchChangesOnlyEnabled()
    {
        var settings = Read("""{ "enabled": false, "keepOnTop": false }""").WithEnabled(true);

        Assert.True(settings.Enabled);
        Assert.False(settings.KeepOnTop);
    }

    private static WindowShadingSettings Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return WindowShadingSettings.Read(document.RootElement.Clone());
    }
}
