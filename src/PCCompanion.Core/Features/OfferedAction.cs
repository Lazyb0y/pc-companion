namespace PCCompanion.Core.Features;

/// <summary>An action that a feature offers to shortcuts and mouse buttons, with the name the settings app shows for it.</summary>
/// <param name="Name">The action's name in the user's language, such as "Hide others" or "Use Office".</param>
public sealed record OfferedAction(FeatureAction Action, string Name)
{
    /// <summary>
    /// Whether it acts on the window in front, as Hide app does. Launchers and the command don't
    /// offer it, since the window in front when it arrives depends on the launcher. The feature's
    /// <see cref="IFeatureActions.OnWindowInFront"/> says the same.
    /// </summary>
    public bool OnWindowInFront { get; init; }
}
