using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCCompanion.Infrastructure.Updates;

/// <summary>
/// The JSON formats of <c>update-check.json</c> and of GitHub's answer, of which only the tag is read.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    Converters = [typeof(ReleaseVersionConverter)])]
[JsonSerializable(typeof(UpdateCheckState))]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;

/// <summary>The part of GitHub's release that's used.</summary>
internal sealed record GitHubRelease([property: JsonPropertyName("tag_name")] string? TagName);

/// <summary>Writes a version as text, such as <c>"1.2.0"</c>, and reads only that form.</summary>
internal sealed class ReleaseVersionConverter : JsonConverter<ReleaseVersion>
{
    public override ReleaseVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && ReleaseVersion.TryParse(reader.GetString(), out var version)
            ? version
            : throw new JsonException("A version must be text such as \"1.2.0\".");

    public override void Write(Utf8JsonWriter writer, ReleaseVersion value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}
