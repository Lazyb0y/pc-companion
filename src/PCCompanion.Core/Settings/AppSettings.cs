using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCCompanion.Core.Settings;

/// <summary>
/// All user settings, stored in <c>settings.json</c>.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// The schema version this build reads and writes. Increase it, and add a migration,
    /// whenever a change would make older settings files read incorrectly.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();

    public DiagnosticsSettings Diagnostics { get; set; } = new();

    public UpdateSettings Updates { get; set; } = new();

    /// <summary>
    /// Each feature's section, by feature ID, in the feature's own format. Sections of features
    /// this build doesn't know are kept.
    /// </summary>
    public Dictionary<string, JsonElement> Features { get; set; } = [];

    public bool Equals(AppSettings? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && General == other.General
        && Diagnostics == other.Diagnostics
        && Updates == other.Updates
        && Features.Count == other.Features.Count
        && Features.All(section => other.Features.TryGetValue(section.Key, out var value) && JsonElement.DeepEquals(section.Value, value));

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, General, Diagnostics, Updates, Features.Count);
}
