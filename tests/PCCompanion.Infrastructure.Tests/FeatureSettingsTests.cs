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

    internal sealed record SampleSettings(bool Enabled, int Delay);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(SampleSettings))]
    internal sealed partial class SampleJsonContext : JsonSerializerContext;
}
