using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// The JSON format of <c>features.minimizeToTray</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(MinimizeToTraySettings))]
internal sealed partial class MinimizeToTrayJsonContext : JsonSerializerContext;
