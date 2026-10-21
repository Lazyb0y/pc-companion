using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>Where the meters show.</summary>
public enum ShowOn
{
    /// <summary>On the taskbar, in one item, the strip, or as notification icons where it can't show.</summary>
    Taskbar,

    /// <summary>As notification icons, one per meter.</summary>
    NotificationArea,
}

/// <summary>How CPU and memory show on the strip.</summary>
public enum StripLayout
{
    /// <summary>Each in its own column: the icon, the value, and below it the name.</summary>
    ValuesAndNames,

    /// <summary>Each in its own column: the icon and the value in large text, without the name.</summary>
    LargeValues,

    /// <summary>Together in one column, CPU above memory, each with a small icon.</summary>
    Compact,
}

/// <summary>The style of the strip's icons.</summary>
public enum StripIcons
{
    /// <summary>Icons drawn in color, which read on light and dark taskbars.</summary>
    Color,

    /// <summary>Thin outline icons in the taskbar's text color.</summary>
    OneColor,

    /// <summary>No icons; the rates keep plain arrows.</summary>
    None,
}

/// <summary>
/// System Meters settings, stored in <c>features.systemMeters</c>. See docs/features/desktop/system-meters.md.
/// </summary>
public sealed record SystemMetersSettings : IFeatureSettings<SystemMetersSettings>
{
    public const int MinimumIntervalSeconds = 1;
    public const int MaximumIntervalSeconds = 10;

    public bool Enabled { get; set; }

    public bool Cpu { get; set; } = true;

    public bool Memory { get; set; } = true;

    public bool Network { get; set; } = true;

    /// <summary>Where the meters show; an unknown choice in the file reads as the default.</summary>
    [JsonConverter(typeof(LenientEnumConverter<ShowOn>))]
    public ShowOn ShowOn { get; set; }

    [JsonConverter(typeof(LenientEnumConverter<StripLayout>))]
    public StripLayout Layout { get; set; }

    [JsonConverter(typeof(LenientEnumConverter<StripIcons>))]
    public StripIcons Icons { get; set; }

    /// <summary>How often the meters update, in seconds; see <see cref="Interval"/>.</summary>
    public int UpdateIntervalSeconds { get; set; } = 1;

    /// <summary>Whether any meter is on.</summary>
    [JsonIgnore]
    public bool AnyMeter => Cpu || Memory || Network;

    /// <summary>How often the meters update, brought into the range of 1 to 10 seconds.</summary>
    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(UpdateIntervalSeconds, MinimumIntervalSeconds, MaximumIntervalSeconds));

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static SystemMetersSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, SystemMetersJsonContext.Default.SystemMetersSettings, () => new SystemMetersSettings());

    /// <summary>
    /// The names of the choices in the section that aren't known, such as a <c>layout</c> written by
    /// a newer version, which <see cref="Read"/> reads as their defaults.
    /// </summary>
    public static IReadOnlyList<string> UnknownChoices(JsonElement? section)
    {
        var unknown = new List<string>();
        if (section is not { ValueKind: JsonValueKind.Object } value)
        {
            return unknown;
        }

        Check<ShowOn>("showOn");
        Check<StripLayout>("layout");
        Check<StripIcons>("icons");
        return unknown;

        void Check<TEnum>(string name)
            where TEnum : struct, Enum
        {
            if (value.TryGetProperty(name, out var choice) && !LenientEnumConverter<TEnum>.TryParse(choice, out _))
            {
                unknown.Add(name);
            }
        }
    }

    public JsonElement Write() => FeatureSettings.Write(this, SystemMetersJsonContext.Default.SystemMetersSettings);

    public SystemMetersSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}

/// <summary>
/// Reads an enum from its name, and an unknown name, such as one from a newer version, as the
/// enum's default; writes the name.
/// </summary>
internal sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public static bool TryParse(JsonElement value, out TEnum choice)
    {
        choice = default;
        return value.ValueKind == JsonValueKind.String && TryParse(value.GetString(), out choice);
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && TryParse(reader.GetString(), out var choice))
        {
            return choice;
        }

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }

    // Only the names: Enum.TryParse would also take numbers.
    private static bool TryParse(string? name, out TEnum choice)
    {
        foreach (var known in Enum.GetValues<TEnum>())
        {
            if (string.Equals(known.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                choice = known;
                return true;
            }
        }

        choice = default;
        return false;
    }
}
