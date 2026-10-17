namespace PCCompanion.Core.Features;

/// <summary>
/// Whether this PC can run a feature.
/// </summary>
/// <param name="ReasonKey">When it can't, the <c>Strings</c> key of a short reason, such as "Needs Windows 11."</param>
public sealed record FeatureSupport(bool IsSupported, string? ReasonKey = null)
{
    public static FeatureSupport Supported { get; } = new(true);

    public static FeatureSupport NotSupported(string reasonKey) => new(false, reasonKey);
}
