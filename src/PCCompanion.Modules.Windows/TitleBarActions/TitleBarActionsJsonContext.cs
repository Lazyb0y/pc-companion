using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// The JSON format of <c>features.titleBarActions</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(TitleBarActionsSettings))]
internal sealed partial class TitleBarActionsJsonContext : JsonSerializerContext;
