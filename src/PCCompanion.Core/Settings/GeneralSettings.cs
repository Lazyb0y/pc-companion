namespace PCCompanion.Core.Settings;

/// <summary>
/// Settings on the General page.
/// </summary>
public sealed record GeneralSettings
{
    /// <summary>
    /// BCP-47 tag of the user interface language, or <see langword="null"/> to follow Windows.
    /// </summary>
    public string? Language { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// Whether the background host keeps running after the user signs in to Windows.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Whether the background host runs as administrator, started by scheduled tasks. Only for
    /// all-users installs; see docs/features/general/run-as-administrator.md.
    /// </summary>
    public bool AlwaysRunAsAdministrator { get; set; }
}
