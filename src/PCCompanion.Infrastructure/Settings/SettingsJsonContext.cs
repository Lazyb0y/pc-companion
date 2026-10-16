using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Settings;

namespace PCCompanion.Infrastructure.Settings;

/// <summary>
/// The JSON format of <c>settings.json</c>. Comments and trailing commas are allowed,
/// so a hand-edited file still loads.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
