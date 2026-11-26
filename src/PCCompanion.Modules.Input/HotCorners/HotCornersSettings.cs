using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

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

    public CornerSettings Top { get; set; } = new();

    public CornerSettings Bottom { get; set; } = new();

    public CornerSettings Left { get; set; } = new();

    public CornerSettings Right { get; set; } = new();

    /// <summary>
    /// How long the pointer stays in a corner before it triggers, from 0 to
    /// <see cref="MaximumActivationDelayMilliseconds"/>.
    /// </summary>
    public int ActivationDelayMilliseconds { get; set; } = DefaultActivationDelayMilliseconds;

    /// <summary>Whether corners trigger after the delay, as by default, or when pushed, as edges do.</summary>
    public CornersTrigger CornersTrigger { get; set; } = CornersTrigger.Delay;

    /// <summary>How hard a push must be, for edges and for corners that trigger when pushed.</summary>
    public PushStrength PushStrength { get; set; } = PushStrength.Medium;

    public bool IgnoreFullScreenApps { get; set; } = true;

    /// <summary>The apps in front of which corners don't trigger.</summary>
    public IReadOnlyList<ListedApp> ExcludedApps { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. Values a
    /// hand-edited file can get wrong are corrected: the delay is kept in range, a corner or edge
    /// with an unknown action, wheel action or key does nothing, an unknown trigger or push strength
    /// gets the default, a blank app ID or program counts as none, and
    /// a listed app without an app ID or a program, or with both, is left out, as is a null in the list.
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
            Top = settings.Top.Checked(),
            Bottom = settings.Bottom.Checked(),
            Left = settings.Left.Checked(),
            Right = settings.Right.Checked(),
            ActivationDelayMilliseconds = Math.Clamp(settings.ActivationDelayMilliseconds, 0, MaximumActivationDelayMilliseconds),
            CornersTrigger = Enum.IsDefined(settings.CornersTrigger) ? settings.CornersTrigger : CornersTrigger.Delay,
            PushStrength = Enum.IsDefined(settings.PushStrength) ? settings.PushStrength : PushStrength.Medium,

            // The JSON reader checks nullable annotations on properties but not on a list's items,
            // so a null in the file's list of apps reads as one.
            ExcludedApps = [.. settings.ExcludedApps.Where(app => app is not null).Select(app => app.Checked()).Where(app => app.IsValid)],
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, HotCornersJsonContext.Default.HotCornersSettings);

    public HotCornersSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether the list has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => ExcludedApps.Any(listed => listed.IsSameAppAs(app));

    /// <summary>
    /// The settings with <paramref name="app"/> at the end of the list, unless it's listed already
    /// or has neither or both of an app ID and a program.
    /// </summary>
    public HotCornersSettings WithExcludedApp(ListedApp app)
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
        && Top == other.Top
        && Bottom == other.Bottom
        && Left == other.Left
        && Right == other.Right
        && ActivationDelayMilliseconds == other.ActivationDelayMilliseconds
        && CornersTrigger == other.CornersTrigger
        && PushStrength == other.PushStrength
        && IgnoreFullScreenApps == other.IgnoreFullScreenApps
        && ExcludedApps.SequenceEqual(other.ExcludedApps);

    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(Enabled, TopLeft, TopRight, BottomLeft, BottomRight),
        HashCode.Combine(Top, Bottom, Left, Right),
        HashCode.Combine(ActivationDelayMilliseconds, CornersTrigger, PushStrength, IgnoreFullScreenApps, ExcludedApps.Count));

    /// <summary>What the corner or edge does.</summary>
    public CornerSettings For(ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => TopLeft,
        ScreenCorner.TopRight => TopRight,
        ScreenCorner.BottomLeft => BottomLeft,
        ScreenCorner.BottomRight => BottomRight,
        ScreenCorner.Top => Top,
        ScreenCorner.Bottom => Bottom,
        ScreenCorner.Left => Left,
        ScreenCorner.Right => Right,
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };

    /// <summary>The settings with <paramref name="corner"/> doing what <paramref name="settings"/> says.</summary>
    public HotCornersSettings With(ScreenCorner corner, CornerSettings settings) => corner switch
    {
        ScreenCorner.TopLeft => this with { TopLeft = settings },
        ScreenCorner.TopRight => this with { TopRight = settings },
        ScreenCorner.BottomLeft => this with { BottomLeft = settings },
        ScreenCorner.BottomRight => this with { BottomRight = settings },
        ScreenCorner.Top => this with { Top = settings },
        ScreenCorner.Bottom => this with { Bottom = settings },
        ScreenCorner.Left => this with { Left = settings },
        ScreenCorner.Right => this with { Right = settings },
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };

    /// <summary>How far the mouse must move, in its own units, for a push to be enough.</summary>
    public int PushCounts => PushStrength switch
    {
        PushStrength.Light => 50,
        PushStrength.Firm => 200,
        _ => 100,
    };
}
