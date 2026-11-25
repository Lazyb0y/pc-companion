using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// The JSON format of <c>features.windowRules</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WindowRulesSettings))]
internal sealed partial class WindowRulesJsonContext : JsonSerializerContext;
