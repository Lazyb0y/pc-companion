using System;

namespace PCCompanion.Modules.Input.Notice;

/// <summary>
/// How opaque the notice is over its time. With Windows' animation effects on, it fades in over
/// 100 ms, shows for its duration, then fades out over 200 ms, unless the Caps Lock badge takes its
/// place; with them off, it shows and hides at once. See "Where the notice shows" in
/// docs/features/input/lock-keys.md.
/// </summary>
public static class NoticeFade
{
    public static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(100);

    public static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(200);

    /// <summary>The time between the steps of a fade, about 60 a second.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// The notice's opacity <paramref name="elapsed"/> after it showed, from 0 to 255, or
    /// <see langword="null"/> once it's gone.
    /// </summary>
    /// <param name="animations">Whether Windows' animation effects are on.</param>
    /// <param name="fadeOut">Whether it fades out at the end, rather than giving way to the badge at once.</param>
    public static byte? Opacity(TimeSpan elapsed, TimeSpan duration, bool animations, bool fadeOut = true)
    {
        if (elapsed < duration)
        {
            return animations && elapsed < FadeIn ? Fraction(elapsed, FadeIn) : byte.MaxValue;
        }

        var fading = elapsed - duration;
        return animations && fadeOut && fading < FadeOut ? (byte)(byte.MaxValue - Fraction(fading, FadeOut)) : null;
    }

    /// <summary>How long until the opacity changes next, or <see langword="null"/> once the notice is gone.</summary>
    public static TimeSpan? NextChange(TimeSpan elapsed, TimeSpan duration, bool animations, bool fadeOut = true)
    {
        if (elapsed < duration)
        {
            return animations && elapsed < FadeIn ? Step : duration - elapsed;
        }

        return animations && fadeOut && elapsed - duration < FadeOut ? Step : null;
    }

    private static byte Fraction(TimeSpan part, TimeSpan whole) => (byte)Math.Round(byte.MaxValue * Math.Clamp(part / whole, 0, 1));
}
