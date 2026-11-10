using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>
/// The JSON format of <c>features.middleClickPaste</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(MiddleClickPasteSettings))]
internal sealed partial class MiddleClickPasteJsonContext : JsonSerializerContext;
