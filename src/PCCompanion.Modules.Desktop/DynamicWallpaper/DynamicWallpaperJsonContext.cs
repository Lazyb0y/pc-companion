using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// The JSON format of <c>features.dynamicWallpaper</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DynamicWallpaperSettings))]
internal sealed partial class DynamicWallpaperJsonContext : JsonSerializerContext;
