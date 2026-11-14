using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// The JSON format of <c>features.remoteFolders</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(RemoteFoldersSettings))]
internal sealed partial class RemoteFoldersJsonContext : JsonSerializerContext;
