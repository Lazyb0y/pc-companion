using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>
/// The JSON format of <c>features.networkProfiles</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(NetworkProfilesSettings))]
internal sealed partial class NetworkProfilesJsonContext : JsonSerializerContext;
