using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// The JSON format of <c>features.appWindows</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(AppWindowsSettings))]
internal sealed partial class AppWindowsJsonContext : JsonSerializerContext;
