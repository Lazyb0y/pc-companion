using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>
/// The JSON format of <c>features.lockKeys</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(LockKeysSettings))]
internal sealed partial class LockKeysJsonContext : JsonSerializerContext;
