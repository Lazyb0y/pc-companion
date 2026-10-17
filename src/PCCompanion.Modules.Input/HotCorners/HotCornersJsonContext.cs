using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// The JSON format of <c>features.hotCorners</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(HotCornersSettings))]
internal sealed partial class HotCornersJsonContext : JsonSerializerContext;
