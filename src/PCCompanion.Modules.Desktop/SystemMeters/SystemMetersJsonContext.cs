using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// The JSON format of <c>features.systemMeters</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(SystemMetersSettings))]
internal sealed partial class SystemMetersJsonContext : JsonSerializerContext;
