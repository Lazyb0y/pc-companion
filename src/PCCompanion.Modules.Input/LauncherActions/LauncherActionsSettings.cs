using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>
/// Launcher Actions settings, stored in <c>features.launcherActions</c>. See
/// docs/features/input/launcher-actions.md.
/// </summary>
public sealed record LauncherActionsSettings : IFeatureSettings<LauncherActionsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>Whether the actions are entries in Start, as Show actions in Start says.</summary>
    public bool ShowInStart { get; set; } = true;

    /// <summary>
    /// The IDs of the features whose actions aren't in Start; the command still runs them. IDs of
    /// features that don't exist, as from a later version, are kept and ignored.
    /// </summary>
    public IReadOnlyList<string> HiddenFeatures { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A null in the list
    /// of hidden features is left out.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static LauncherActionsSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, LauncherActionsJsonContext.Default.LauncherActionsSettings, () => new LauncherActionsSettings());

        // The JSON reader checks nullable annotations on properties but not on a list's items.
        return settings with { HiddenFeatures = [.. settings.HiddenFeatures.Where(featureId => featureId is not null)] };
    }

    public JsonElement Write() => FeatureSettings.Write(this, LauncherActionsJsonContext.Default.LauncherActionsSettings);

    public LauncherActionsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the actions of the feature with the ID <paramref name="featureId"/> are entries in Start.</summary>
    public bool IsInStart(string featureId) => Enabled && ShowInStart && !HiddenFeatures.Contains(featureId, StringComparer.Ordinal);

    // A record compares lists by reference; settings compare by value.
    public bool Equals(LauncherActionsSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && ShowInStart == other.ShowInStart
        && HiddenFeatures.SequenceEqual(other.HiddenFeatures, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Enabled, ShowInStart, HiddenFeatures.Count);
}

/// <summary>The JSON format of <c>features.launcherActions</c>, matching the rest of <c>settings.json</c>.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LauncherActionsSettings))]
internal sealed partial class LauncherActionsJsonContext : JsonSerializerContext;
