namespace PCCompanion.Core.Features;

/// <summary>
/// An action of a feature that a shortcut or a mouse button can run, such as Hide Apps' Hide others.
/// See "How features offer actions" in docs/features/input/shortcuts-and-mouse-buttons.md.
/// </summary>
/// <param name="FeatureId">The ID of the feature that offers it, such as <c>hideApps</c>.</param>
/// <param name="ActionId">The feature's own ID for it, such as <c>hideOthers</c>.</param>
/// <param name="Target">
/// What it acts on, for an action on a profile or an app: the profile's name, or the app's app ID
/// or program, as the feature that offers it stores them; otherwise <see langword="null"/>.
/// </param>
public sealed record FeatureAction(string FeatureId, string ActionId, string? Target = null);
