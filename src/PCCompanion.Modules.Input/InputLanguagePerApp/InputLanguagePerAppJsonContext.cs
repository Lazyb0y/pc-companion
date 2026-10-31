using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// The JSON format of <c>features.inputLanguagePerApp</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(InputLanguagePerAppSettings))]
internal sealed partial class InputLanguagePerAppJsonContext : JsonSerializerContext;
