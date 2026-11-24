using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// The JSON format of <c>features.windowTabs</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(WindowTabsSettings))]
internal sealed partial class WindowTabsJsonContext : JsonSerializerContext;
