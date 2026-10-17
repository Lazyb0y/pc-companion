using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed partial class FeatureSettingsTests
{
    [Fact]
    public void ReadReturnsTheDefaultsWithoutASection()
    {
        Assert.Equal(new SampleSettings(false, 150), FeatureSettings.Read(null, SampleJsonContext.Default.SampleSettings, () => new SampleSettings(false, 150)));
    }

    [Fact]
    public void WriteThenReadReturnsTheSameSettings()
    {
        var section = FeatureSettings.Write(new SampleSettings(true, 300), SampleJsonContext.Default.SampleSettings);

        Assert.Equal("""{"enabled":true,"delay":300}""", section.GetRawText());
        Assert.Equal(new SampleSettings(true, 300), FeatureSettings.Read(section, SampleJsonContext.Default.SampleSettings, () => new SampleSettings(false, 0)));
    }

    [Fact]
    public void ReadRejectsASectionThatDoesNotMatch()
    {
        using var document = JsonDocument.Parse("""{ "enabled": "yes" }""");

        Assert.ThrowsAny<JsonException>(() => FeatureSettings.Read(document.RootElement, SampleJsonContext.Default.SampleSettings, () => new SampleSettings(false, 0)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("""{ "enabled": true }""", true)]
    [InlineData("""{ "enabled": false }""", false)]
    [InlineData("""{ "enabled": "yes" }""", false)]
    [InlineData("""{ "other": 1 }""", false)]
    [InlineData("""[true]""", false)]
    public void IsEnabledReadsTheEnabledFlag(string? json, bool expected)
    {
        Assert.Equal(expected, FeatureSettings.IsEnabled(json is null ? null : Section(json)));
    }

    [Fact]
    public void SetEnabledKeepsTheOtherSettings()
    {
        var section = FeatureSettings.SetEnabled(Section("""{ "enabled": false, "delay": 300, "corner": { "action": "Lock" } }"""), enabled: true);

        Assert.Equal("""{"enabled":true,"delay":300,"corner":{"action":"Lock"}}""", section.GetRawText());
    }

    [Fact]
    public void SetEnabledCreatesASectionWhenThereIsNone()
    {
        Assert.Equal("""{"enabled":true}""", FeatureSettings.SetEnabled(null, enabled: true).GetRawText());
    }

    private static JsonElement Section(string json) => JsonDocument.Parse(json).RootElement.Clone();

    internal sealed record SampleSettings(bool Enabled, int Delay);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(SampleSettings))]
    internal sealed partial class SampleJsonContext : JsonSerializerContext;
}
