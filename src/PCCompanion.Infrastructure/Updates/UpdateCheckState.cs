using System;

namespace PCCompanion.Infrastructure.Updates;

/// <summary>
/// The result of the update checks, kept in <c>update-check.json</c>. The host writes it, and the
/// settings app after Check now.
/// </summary>
public sealed record UpdateCheckState
{
    /// <summary>When GitHub last answered, or <see langword="null"/> before the first answer.</summary>
    public DateTimeOffset? LastCheck { get; init; }

    /// <summary>The newest version GitHub named, or <see langword="null"/> when there is no release.</summary>
    public ReleaseVersion? LatestVersion { get; init; }

    /// <summary>The last version the notification showed.</summary>
    public ReleaseVersion? NotifiedVersion { get; init; }

    /// <summary>
    /// The version to tell the user about, when the newest one is newer than
    /// <paramref name="current"/>; otherwise <see langword="null"/>.
    /// </summary>
    public ReleaseVersion? NewerThan(ReleaseVersion current) => LatestVersion > current ? LatestVersion : null;
}
