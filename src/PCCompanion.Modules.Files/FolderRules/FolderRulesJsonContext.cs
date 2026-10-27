using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// The JSON format of <c>features.folderRules</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(FolderRulesSettings))]
internal sealed partial class FolderRulesJsonContext : JsonSerializerContext;
