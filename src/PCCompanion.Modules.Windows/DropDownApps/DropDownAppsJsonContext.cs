using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// The JSON format of <c>features.dropDownApps</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DropDownAppsSettings))]
internal sealed partial class DropDownAppsJsonContext : JsonSerializerContext;
