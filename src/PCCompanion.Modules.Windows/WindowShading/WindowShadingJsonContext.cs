using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// The JSON format of <c>features.windowShading</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(WindowShadingSettings))]
internal sealed partial class WindowShadingJsonContext : JsonSerializerContext;
