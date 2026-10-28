using System;
using System.Text.Json;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>
/// Lock Keys settings, stored in <c>features.lockKeys</c>. See docs/features/input/lock-keys.md.
/// </summary>
public sealed record LockKeysSettings : IFeatureSettings<LockKeysSettings>
{
    public const int DefaultDurationMilliseconds = 2000;

    public const int MinimumDurationMilliseconds = 1000;

    public const int MaximumDurationMilliseconds = 5000;

    public bool Enabled { get; set; }

    public bool CapsLock { get; set; } = true;

    public bool NumLock { get; set; } = true;

    /// <summary>
    /// Off by default: few programs use it, and many KVM switches switch computers on two quick
    /// presses of it.
    /// </summary>
    public bool ScrollLock { get; set; }

    /// <summary>Whether a key turning off shows a notice too, not only a key turning on.</summary>
    public bool ShowWhenOff { get; set; } = true;

    public NoticePosition Position { get; set; } = NoticePosition.BottomCenter;

    /// <summary>
    /// How long the notice shows, from <see cref="MinimumDurationMilliseconds"/> to
    /// <see cref="MaximumDurationMilliseconds"/>.
    /// </summary>
    public int DurationMilliseconds { get; set; } = DefaultDurationMilliseconds;

    public bool IgnoreFullScreenApps { get; set; } = true;

    /// <summary>Whether a badge stays on screen while Caps Lock is on.</summary>
    public bool KeepCapsLockBadge { get; set; }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. Values a
    /// hand-edited file can get wrong are corrected: the duration is kept in range, and an
    /// unknown position is the bottom center.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static LockKeysSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, LockKeysJsonContext.Default.LockKeysSettings, () => new LockKeysSettings());

        return settings with
        {
            // A number in the file reads as any enum value, named or not.
            Position = Enum.IsDefined(settings.Position) ? settings.Position : NoticePosition.BottomCenter,
            DurationMilliseconds = Math.Clamp(settings.DurationMilliseconds, MinimumDurationMilliseconds, MaximumDurationMilliseconds),
        };
    }

    public JsonElement Write() => FeatureSettings.Write(this, LockKeysJsonContext.Default.LockKeysSettings);

    public LockKeysSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    /// <summary>Whether <paramref name="key"/> turning on or off shows a notice.</summary>
    public bool Shows(LockKey key) => key switch
    {
        LockKey.CapsLock => CapsLock,
        LockKey.NumLock => NumLock,
        LockKey.ScrollLock => ScrollLock,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
