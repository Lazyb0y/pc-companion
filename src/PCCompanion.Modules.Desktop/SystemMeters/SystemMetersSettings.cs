using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// System Meters settings, stored in <c>features.systemMeters</c>. See docs/features/desktop/system-meters.md.
/// </summary>
public sealed record SystemMetersSettings
{
    public const int MinimumIntervalSeconds = 1;
    public const int MaximumIntervalSeconds = 10;

    public bool Enabled { get; set; }

    public bool Cpu { get; set; } = true;

    public bool Memory { get; set; } = true;

    public bool Network { get; set; } = true;

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

    public JsonElement Write() => FeatureSettings.Write(this, SystemMetersJsonContext.Default.SystemMetersSettings);
}
