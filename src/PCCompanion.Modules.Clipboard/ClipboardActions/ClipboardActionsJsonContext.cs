using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>
/// The JSON format of <c>features.clipboardActions</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(ClipboardActionsSettings))]
internal sealed partial class ClipboardActionsJsonContext : JsonSerializerContext;
