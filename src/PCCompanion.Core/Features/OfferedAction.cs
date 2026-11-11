namespace PCCompanion.Core.Features;

/// <summary>An action that a feature offers to shortcuts and mouse buttons, with the name the settings app shows for it.</summary>
/// <param name="Name">The action's name in the user's language, such as "Hide others" or "Use Office".</param>
public sealed record OfferedAction(FeatureAction Action, string Name);
