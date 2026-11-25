using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// Window Shading settings, stored in <c>features.windowShading</c>. See docs/features/windows/window-shading.md.
/// </summary>
public sealed record WindowShadingSettings : IFeatureSettings<WindowShadingSettings>
{
    public bool Enabled { get; set; }

    /// <summary>Whether rolled-up title bars stay above other windows.</summary>
    public bool KeepOnTop { get; set; } = true;

    /// <summary>Hover to roll down.</summary>
    public ShadingHover Hover { get; set; } = new();

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static WindowShadingSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, WindowShadingJsonContext.Default.WindowShadingSettings, () => new WindowShadingSettings());

    public JsonElement Write() => FeatureSettings.Write(this, WindowShadingJsonContext.Default.WindowShadingSettings);

    public WindowShadingSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}

/// <summary>
/// Hover to roll down: a window rolls down while the pointer rests on its title bar, and up again
/// when the pointer leaves. The delays are kept as written, so a value this version doesn't offer
/// is kept when the settings are saved, and its default is used.
/// </summary>
public sealed record ShadingHover
{
    /// <summary>The delays offered for rolling down, in milliseconds.</summary>
    public static IReadOnlyList<int> RollDownChoices { get; } = [250, 500, 1000, 2000];

    /// <summary>The delays offered for rolling up again, in milliseconds.</summary>
    public static IReadOnlyList<int> RollUpChoices { get; } = [500, 1000, 2000, 5000];

    public const int DefaultRollDownAfterMs = 500;

    public const int DefaultRollUpAfterMs = 1000;

    public bool Enabled { get; set; }

    /// <summary>How long the pointer rests on a title bar before its window rolls down.</summary>
    public int RollDownAfterMs { get; set; } = DefaultRollDownAfterMs;

    /// <summary>How long the pointer has been off the window before it rolls up again.</summary>
    public int RollUpAfterMs { get; set; } = DefaultRollUpAfterMs;

    /// <summary>The delay before rolling down: the one set, if it's offered, or the default.</summary>
    [JsonIgnore]
    public TimeSpan RollDownAfter => TimeSpan.FromMilliseconds(RollDownChoices.Contains(RollDownAfterMs) ? RollDownAfterMs : DefaultRollDownAfterMs);

    /// <summary>The delay before rolling up again: the one set, if it's offered, or the default.</summary>
    [JsonIgnore]
    public TimeSpan RollUpAfter => TimeSpan.FromMilliseconds(RollUpChoices.Contains(RollUpAfterMs) ? RollUpAfterMs : DefaultRollUpAfterMs);
}
