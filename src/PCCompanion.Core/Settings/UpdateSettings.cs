namespace PCCompanion.Core.Settings;

/// <summary>
/// Settings for finding out about new versions; see docs/features/general/update-check.md.
/// </summary>
public sealed record UpdateSettings
{
    /// <summary>
    /// Whether the host asks GitHub once a day whether a newer version is available. On by
    /// default, also when the settings file has no value for it.
    /// </summary>
    public bool CheckAutomatically { get; set; } = true;
}
