using System.Text.Json;
using PCCompanion.Modules.Input.LauncherActions;
using Xunit;
using static PCCompanion.Testing.TestJson;

namespace PCCompanion.Modules.Input.Tests;

public sealed class LauncherActionsSettingsTests
{
    // The example in docs/features/input/launcher-actions.md.
    private const string SpecExample = """
        {
          "enabled": true,
          "showInStart": true,
          "hiddenFeatures": [ "endApp" ]
        }
        """;

    [Fact]
    public void WithoutASectionTheFeatureIsOffWithEveryFeatureInStart()
    {
        var settings = LauncherActionsSettings.Read(null);

        Assert.False(settings.Enabled);
        Assert.True(settings.ShowInStart);
        Assert.Empty(settings.HiddenFeatures);
    }

    [Fact]
    public void TurnedOnWithoutMoreEveryFeaturesActionsAreInStart()
    {
        var settings = LauncherActionsSettings.Read(Section("""{ "enabled": true }"""));

        Assert.True(settings.IsInStart("networkProfiles"));
        Assert.True(settings.IsInStart("endApp"));
    }

    [Fact]
    public void TheSpecsExampleLeavesEndAppOutOfStart()
    {
        var settings = LauncherActionsSettings.Read(Section(SpecExample));

        Assert.True(settings.Enabled);
        Assert.True(settings.IsInStart("networkProfiles"));
        Assert.False(settings.IsInStart("endApp"));
    }

    [Fact]
    public void NothingIsInStartWhileTheFeatureOrShowActionsInStartIsOff()
    {
        Assert.False(LauncherActionsSettings.Read(Section("""{ "enabled": false }""")).IsInStart("networkProfiles"));
        Assert.False(LauncherActionsSettings.Read(Section("""{ "enabled": true, "showInStart": false }""")).IsInStart("networkProfiles"));
    }

    [Fact]
    public void UnknownFeaturesAreKeptAndNullsLeftOut()
    {
        var settings = LauncherActionsSettings.Read(Section("""{ "enabled": true, "hiddenFeatures": [ null, "laterFeature", "endApp" ] }"""));

        Assert.Equal(["laterFeature", "endApp"], settings.HiddenFeatures);
    }

    [Fact]
    public void TheSettingsReadBackAsWrittenAndEqual()
    {
        var settings = LauncherActionsSettings.Read(Section(SpecExample));

        Assert.Equal(settings, LauncherActionsSettings.Read(settings.Write()));
        Assert.Equal(settings.GetHashCode(), LauncherActionsSettings.Read(Section(SpecExample)).GetHashCode());
        Assert.NotEqual(settings, settings with { HiddenFeatures = ["dropDownApps"] });
    }

    [Fact]
    public void TheFormatMatchesTheSpec()
    {
        var json = JsonSerializer.Serialize(new LauncherActionsSettings { Enabled = true, HiddenFeatures = ["endApp"] }.Write());

        Assert.Equal("""{"enabled":true,"showInStart":true,"hiddenFeatures":["endApp"]}""", json);
    }

    [Theory]
    [InlineData("""{ "enabled": true, "showInStart": "yes" }""")]
    [InlineData("""{ "enabled": true, "hiddenFeatures": "endApp" }""")]
    [InlineData("""{ "enabled": true, "hiddenFeatures": null }""")]
    public void ASectionThatDoesntMatchTheFormatCantBeRead(string section) =>
        Assert.Throws<JsonException>(() => LauncherActionsSettings.Read(Section(section)));

    [Fact]
    public void TurningItOnOrOffKeepsTheRest()
    {
        var settings = LauncherActionsSettings.Read(Section(SpecExample));

        Assert.Equal(settings with { Enabled = false }, settings.WithEnabled(false));
    }
}
