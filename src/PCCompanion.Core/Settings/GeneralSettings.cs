namespace PCCompanion.Core.Settings;

/// <summary>
/// Settings on the General page.
/// </summary>
public sealed record GeneralSettings
{
    /// <summary>
    /// BCP-47 tag of the user interface language, or <see langword="null"/> to follow Windows.
    /// </summary>
    public string? Language { get; init; }

    public AppTheme Theme { get; init; } = AppTheme.System;
}
