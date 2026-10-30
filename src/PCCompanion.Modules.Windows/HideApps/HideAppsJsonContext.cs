using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.HideApps;

/// <summary>
/// The JSON format of <c>features.hideApps</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(HideAppsSettings))]
internal sealed partial class HideAppsJsonContext : JsonSerializerContext;
