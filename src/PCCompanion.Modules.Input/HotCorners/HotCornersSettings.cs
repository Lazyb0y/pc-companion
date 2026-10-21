using System;
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

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. Values a
    /// hand-edited file can get wrong are corrected: the delay is kept in range and a corner
    /// with an unknown action or key does nothing.
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
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, HotCornersJsonContext.Default.HotCornersSettings);

    public HotCornersSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    public CornerSettings For(ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => TopLeft,
        ScreenCorner.TopRight => TopRight,
        ScreenCorner.BottomLeft => BottomLeft,
        ScreenCorner.BottomRight => BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };
}
