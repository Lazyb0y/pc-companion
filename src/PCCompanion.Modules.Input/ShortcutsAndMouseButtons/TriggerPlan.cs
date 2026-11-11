using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// What the feature's thread registers and claims: the triggers that can be used, whose actions
/// can run now, and the full-screen pause. The host's UI thread works it out at each change to
/// the settings and after the host applied them to every feature.
/// </summary>
public sealed record TriggerPlan(IReadOnlyList<PlannedTrigger> Triggers, bool PauseMouseButtonsInFullScreen)
{
    // A record compares lists by reference; plans compare by value, so an unchanged plan isn't applied again.
    public bool Equals(TriggerPlan? other) =>
        other is not null && PauseMouseButtonsInFullScreen == other.PauseMouseButtonsInFullScreen && Triggers.SequenceEqual(other.Triggers);

    public override int GetHashCode() => HashCode.Combine(PauseMouseButtonsInFullScreen, Triggers.Count);
}

/// <summary>A trigger that can be used, with what it's pressed with and the apps it skips.</summary>
/// <param name="Index">Its place in the settings' list.</param>
public sealed record PlannedTrigger(int Index, FeatureAction Action, TriggerInput Input, IReadOnlyList<ListedApp> ExcludedApps)
{
    /// <summary>The shortcut by its key's position, or <see langword="null"/> for a mouse button.</summary>
    public KeyPositionShortcut? Shortcut =>
        Input.Key is { } key && KeyCodes.ScanCodeOf(key) is { } scanCode ? new KeyPositionShortcut(Input.Modifiers, scanCode) : null;

    public bool Equals(PlannedTrigger? other) =>
        other is not null && Index == other.Index && Action == other.Action && Input == other.Input && ExcludedApps.SequenceEqual(other.ExcludedApps);

    public override int GetHashCode() => HashCode.Combine(Index, Action, Input, ExcludedApps.Count);
}
