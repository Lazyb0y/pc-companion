using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>
/// The JSON format of <c>features.displayProfiles</c>, matching the rest of <c>settings.json</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DisplayProfilesSettings))]
internal sealed partial class DisplayProfilesJsonContext : JsonSerializerContext;

/// <summary>
/// The format, with a display's place and mode written only while it's on, so a display that's
/// off has only what recognizes and names it.
/// </summary>
internal static class DisplayProfilesFormat
{
    private static readonly HashSet<string> OnlyWhenOn =
    [
        .. new[]
        {
            nameof(ProfileDisplay.Main),
            nameof(ProfileDisplay.X),
            nameof(ProfileDisplay.Y),
            nameof(ProfileDisplay.Width),
            nameof(ProfileDisplay.Height),
            nameof(ProfileDisplay.RefreshRate),
            nameof(ProfileDisplay.Orientation),
            nameof(ProfileDisplay.Hdr),
        }.Select(JsonNamingPolicy.CamelCase.ConvertName),
    ];

    public static JsonTypeInfo<DisplayProfilesSettings> Settings { get; } = Create();

    private static JsonTypeInfo<DisplayProfilesSettings> Create()
    {
        var options = new JsonSerializerOptions(DisplayProfilesJsonContext.Default.Options)
        {
            TypeInfoResolver = DisplayProfilesJsonContext.Default.WithAddedModifier(type =>
            {
                if (type.Type != typeof(ProfileDisplay))
                {
                    return;
                }

                foreach (var property in type.Properties)
                {
                    if (OnlyWhenOn.Contains(property.Name))
                    {
                        property.ShouldSerialize = (display, _) => ((ProfileDisplay)display).On;
                    }
                }
            }),
        };
        return (JsonTypeInfo<DisplayProfilesSettings>)options.GetTypeInfo(typeof(DisplayProfilesSettings));
    }
}
