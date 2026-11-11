using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.ShortcutsAndMouseButtons;

/// <summary>
/// A shortcut or a mouse button that runs one of PC Companion's actions, as the settings store it.
/// It has a shortcut or a mouse button, not both; see <see cref="TriggerCheck"/> for what makes
/// one usable.
/// </summary>
public sealed record Trigger
{
    public TriggerAction Action { get; set; } = new();

    public TriggerShortcut? Shortcut { get; set; }

    public TriggerMouseButton? MouseButton { get; set; }

    /// <summary>
    /// The apps the trigger skips, so they get the keys or the click themselves: the app in front
    /// for a shortcut, the app under the pointer for a mouse button.
    /// </summary>
    public IReadOnlyList<ListedApp> ExcludedApps { get; set; } = [];

    // A record compares lists by reference; settings compare by value.
    public bool Equals(Trigger? other) =>
        other is not null
        && Action == other.Action
        && Shortcut == other.Shortcut
        && MouseButton == other.MouseButton
        && ExcludedApps.SequenceEqual(other.ExcludedApps);

    public override int GetHashCode() => HashCode.Combine(Action, Shortcut, MouseButton, ExcludedApps.Count);

    /// <summary>
    /// The trigger as read from a hand-editable file: blank IDs and a blank target read as none,
    /// and a null, a blank app ID or program, or a listed app with neither or both, is left out.
    /// </summary>
    public Trigger Checked() => this with
    {
        Action = (Action ?? new TriggerAction()).Checked(),
        Shortcut = Shortcut?.Checked(),
        MouseButton = MouseButton?.Checked(),

        // The JSON reader checks nullable annotations on properties but not on a list's items.
        ExcludedApps = [.. (ExcludedApps ?? []).Where(app => app is not null).Select(app => app.Checked()).Where(app => app.IsValid)],
    };

    /// <summary>Whether the list of apps the trigger skips has <paramref name="app"/> already.</summary>
    public bool Lists(ListedApp app) => ExcludedApps.Any(listed => listed.IsSameAppAs(app));
}

/// <summary>The action a trigger runs: a feature's ID, the feature's ID for the action, and what it acts on.</summary>
public sealed record TriggerAction
{
    public string Feature { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    /// <summary>A profile's name, or a Drop-down app's app ID or program; none for most actions.</summary>
    public string? Target { get; set; }

    /// <summary>The action, as a feature runs it.</summary>
    public FeatureAction ToFeatureAction() => new(Feature, Id, Target);

    /// <summary>The settings' form of <paramref name="action"/>.</summary>
    public static TriggerAction For(FeatureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new TriggerAction { Feature = action.FeatureId, Id = action.ActionId, Target = action.Target };
    }

    internal TriggerAction Checked() => this with
    {
        Feature = (Feature ?? string.Empty).Trim(),
        Id = (Id ?? string.Empty).Trim(),
        Target = string.IsNullOrWhiteSpace(Target) ? null : Target,
    };
}

/// <summary>A shortcut: the modifier keys, by name, and the key, by its position, as <see cref="KeyCodes"/> names it.</summary>
public sealed record TriggerShortcut
{
    /// <summary>Any of <c>Control</c>, <c>Alt</c>, <c>Shift</c> and <c>Windows</c>.</summary>
    public IReadOnlyList<string> Modifiers { get; set; } = [];

    /// <summary>A <c>KeyboardEvent.code</c> name, such as <c>KeyH</c> or <c>F13</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public bool Equals(TriggerShortcut? other) => other is not null && Key == other.Key && Modifiers.SequenceEqual(other.Modifiers);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers.Count);

    /// <summary>The shortcut for <paramref name="modifiers"/> and the key named <paramref name="key"/>, with the modifiers in their usual order.</summary>
    public static TriggerShortcut For(HotKeyModifiers modifiers, string key) => new() { Modifiers = TriggerModifiers.NamesOf(modifiers), Key = key };

    internal TriggerShortcut Checked() => this with
    {
        Modifiers = [.. (Modifiers ?? []).Where(name => name is not null)],
        Key = (Key ?? string.Empty).Trim(),
    };
}

/// <summary>A mouse button and the keys held with it.</summary>
public sealed record TriggerMouseButton
{
    /// <summary>
    /// A number in the file reads as any value, named or not; <see cref="TriggerCheck"/> finds an
    /// unknown one.
    /// </summary>
    public TriggerButton Button { get; set; }

    /// <summary>Any of <c>Control</c>, <c>Alt</c>, <c>Shift</c> and <c>Windows</c>.</summary>
    public IReadOnlyList<string> Modifiers { get; set; } = [];

    public bool Equals(TriggerMouseButton? other) => other is not null && Button == other.Button && Modifiers.SequenceEqual(other.Modifiers);

    public override int GetHashCode() => HashCode.Combine(Button, Modifiers.Count);

    /// <summary>The mouse button <paramref name="button"/> with <paramref name="modifiers"/>, in their usual order.</summary>
    public static TriggerMouseButton For(TriggerButton button, HotKeyModifiers modifiers) => new() { Button = button, Modifiers = TriggerModifiers.NamesOf(modifiers) };

    internal TriggerMouseButton Checked() => this with { Modifiers = [.. (Modifiers ?? []).Where(name => name is not null)] };
}

/// <summary>The mouse buttons a trigger can use.</summary>
public enum TriggerButton
{
    /// <summary>The Back button, <c>XBUTTON1</c>.</summary>
    Back,

    /// <summary>The Forward button, <c>XBUTTON2</c>.</summary>
    Forward,

    /// <summary>The middle button, only with the Windows key.</summary>
    Middle,
}

/// <summary>The modifier keys' names in the settings.</summary>
public static class TriggerModifiers
{
    private static readonly (string Name, HotKeyModifiers Modifier)[] Names =
    [
        ("Control", HotKeyModifiers.Control),
        ("Alt", HotKeyModifiers.Alt),
        ("Shift", HotKeyModifiers.Shift),
        ("Windows", HotKeyModifiers.Windows),
    ];

    /// <summary>The modifiers named in <paramref name="names"/>, ignoring case, or <see langword="null"/> when one is unknown.</summary>
    public static HotKeyModifiers? ModifiersOf(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var modifiers = HotKeyModifiers.None;
        foreach (var name in names)
        {
            var known = Array.FindIndex(Names, entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
            if (known < 0)
            {
                return null;
            }

            modifiers |= Names[known].Modifier;
        }

        return modifiers;
    }

    /// <summary>The names of <paramref name="modifiers"/>, in the order Ctrl, Alt, Shift, Windows.</summary>
    public static IReadOnlyList<string> NamesOf(HotKeyModifiers modifiers) =>
        [.. Names.Where(entry => modifiers.HasFlag(entry.Modifier)).Select(entry => entry.Name)];
}
