using System;

namespace PCCompanion.Core.Settings;

/// <summary>
/// Settings for diagnosing problems.
/// </summary>
public sealed record DiagnosticsSettings
{
    /// <summary>
    /// When detailed logging turns off, or <see langword="null"/> when it is off. Turning it on
    /// sets a time 24 hours ahead, so it turns itself off.
    /// </summary>
    public DateTimeOffset? DetailedLoggingUntil { get; set; }
}
