using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// The JSON format of <c>features.driveActions</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DriveActionsSettings))]
internal sealed partial class DriveActionsJsonContext : JsonSerializerContext;
