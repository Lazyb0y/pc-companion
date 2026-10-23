using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Hot Corners settings, stored in <c>features.hotCorners</c>. See docs/features/input/hot-corners.md.
/// </summary>
public sealed record HotCornersSettings : IFeatureSettings<HotCornersSettings>
{
    public const int DefaultActivationDelayMilliseconds = 150;

    public const int MaximumActivationDelayMilliseconds = 1000;

    public bool Enabled { get; set; }

    /// <summary>
    /// Task View by default, so the feature does something as soon as it's turned on.
    /// </summary>
    public CornerSettings TopLeft { get; set; } = new() { Action = HotCornerAction.TaskView };

    public CornerSettings TopRight { get; set; } = new();

    public CornerSettings BottomLeft { get; set; } = new();

    public CornerSettings BottomRight { get; set; } = new();

    /// <summary>
    /// How long the pointer stays in a corner before it triggers, from 0 to
    /// <see cref="MaximumActivationDelayMilliseconds"/>.
    /// </summary>
    public int ActivationDelayMilliseconds { get; set; } = DefaultActivationDelayMilliseconds;

    public bool IgnoreFullScreenApps { get; set; } = true;

    /// <summary>The apps in front of which corners don't trigger.</summary>
    public IReadOnlyList<ExcludedApp> ExcludedApps { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. Values a
    /// hand-edited file can get wrong are corrected: the delay is kept in range, a corner
    /// with an unknown action or key does nothing, and a listed app without an app ID or a
    /// program, or with both, is left out.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static HotCornersSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, HotCornersJsonContext.Default.HotCornersSettings, () => new HotCornersSettings());

        return settings with
        {
            TopLeft = settings.TopLeft.Checked(),
            TopRight = settings.TopRight.Checked(),
            BottomLeft = settings.BottomLeft.Checked(),
            BottomRight = settings.BottomRight.Checked(),
            ActivationDelayMilliseconds = Math.Clamp(settings.ActivationDelayMilliseconds, 0, MaximumActivationDelayMilliseconds),
            ExcludedApps = [.. settings.ExcludedApps.Where(app => app.IsValid)],
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, HotCornersJsonContext.Default.HotCornersSettings);

    public HotCornersSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ExcludedApp app) => ExcludedApps.Any(listed => listed.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, unless it's listed already
    /// or has neither or both of an app ID and a program.
    /// </summary>
    public HotCornersSettings WithExcludedApp(ExcludedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return !app.IsValid || Lists(app) ? this : this with { ExcludedApps = [.. ExcludedApps, app] };
    }

    /// <summary>The settings without the listed app at <paramref name="index"/>.</summary>
    public HotCornersSettings WithoutExcludedApp(int index) =>
        this with { ExcludedApps = [.. ExcludedApps.Where((_, at) => at != index)] };

    // A record compares lists by reference; settings compare by value.
    public bool Equals(HotCornersSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && TopLeft == other.TopLeft
        && TopRight == other.TopRight
        && BottomLeft == other.BottomLeft
        && BottomRight == other.BottomRight
        && ActivationDelayMilliseconds == other.ActivationDelayMilliseconds
        && IgnoreFullScreenApps == other.IgnoreFullScreenApps
        && ExcludedApps.SequenceEqual(other.ExcludedApps);

    public override int GetHashCode() => HashCode.Combine(Enabled, TopLeft, TopRight, BottomLeft, BottomRight, ActivationDelayMilliseconds, IgnoreFullScreenApps, ExcludedApps.Count);

    public CornerSettings For(ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => TopLeft,
        ScreenCorner.TopRight => TopRight,
        ScreenCorner.BottomLeft => BottomLeft,
        ScreenCorner.BottomRight => BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };
}
