using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PCCompanion.Core.Features;

/// <summary>
/// Reads and writes a feature's section of the settings with the feature's own JSON format.
/// </summary>
public static class FeatureSettings
{
    private const string EnabledProperty = "enabled";

    /// <summary>
    /// Returns the settings in <paramref name="section"/>, or <paramref name="defaults"/> when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static T Read<T>(JsonElement? section, JsonTypeInfo<T> format, Func<T> defaults)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(defaults);

        return section is { } value ? value.Deserialize(format) ?? defaults() : defaults();
    }

    public static JsonElement Write<T>(T settings, JsonTypeInfo<T> format) =>
        JsonSerializer.SerializeToElement(settings, format);

    /// <summary>
    /// Whether the feature is on, read from any feature's section without knowing its format.
    /// </summary>
    public static bool IsEnabled(JsonElement? section) =>
        section is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(EnabledProperty, out var enabled)
        && enabled.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Returns <paramref name="section"/> with the feature turned on or off and its other settings
    /// unchanged, or a new section when there is none.
    /// </summary>
    public static JsonElement SetEnabled(JsonElement? section, bool enabled)
    {
        var node = section is { ValueKind: JsonValueKind.Object } value ? JsonObject.Create(value)! : [];
        node[EnabledProperty] = enabled;

        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
