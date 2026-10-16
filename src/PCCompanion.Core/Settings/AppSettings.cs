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

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public GeneralSettings General { get; init; } = new();
}
