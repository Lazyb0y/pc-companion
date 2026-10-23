using System;
using System.Text.Json.Serialization;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// An app in front of which corners don't trigger. It has an app ID or a program, not both. See
/// "Apps that turn corners off" in docs/features/input/hot-corners.md.
/// </summary>
public sealed record ExcludedApp
{
    /// <summary>The name the page shows, as it was when the app was added.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The app ID (AppUserModelID) that the app's windows or its Store package have.</summary>
    public string? AppId { get; set; }

    /// <summary>The full path of the app's program file, for an app without an app ID.</summary>
    public string? Program { get; set; }

    /// <summary>Whether the app has an app ID or a program, but not both.</summary>
    [JsonIgnore]
    public bool IsValid => string.IsNullOrWhiteSpace(AppId) != string.IsNullOrWhiteSpace(Program);

    /// <summary>
    /// Whether this is the app in front: its app ID is the one the window sets or its package's,
    /// or its program is the window's program, whatever their case.
    /// </summary>
    public bool Matches(AppInFront app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return AppId is { } id
            ? string.Equals(id, app.WindowAppId, StringComparison.OrdinalIgnoreCase) || string.Equals(id, app.PackageAppId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Program, app.Program, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="other"/> has the same app ID or program, whatever its case.</summary>
    public bool IsSameAppAs(ExcludedApp other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return AppId is not null
            ? string.Equals(AppId, other.AppId, StringComparison.OrdinalIgnoreCase)
            : Program is not null && string.Equals(Program, other.Program, StringComparison.OrdinalIgnoreCase);
    }

    // A hand-edited file can hold a blank app ID or program, which counts as none.
    internal ExcludedApp Checked() => this with
    {
        AppId = string.IsNullOrWhiteSpace(AppId) ? null : AppId,
        Program = string.IsNullOrWhiteSpace(Program) ? null : Program,
    };
}

/// <summary>
/// The app of the window in front, as Hot Corners compares it with the listed apps.
/// </summary>
/// <param name="WindowAppId">The app ID the window sets for itself, as browsers do for each profile.</param>
/// <param name="PackageAppId">The app ID of the window's Store app package.</param>
/// <param name="Program">The full path of the window's program file.</param>
public sealed record AppInFront(string? WindowAppId, string? PackageAppId, string? Program);
