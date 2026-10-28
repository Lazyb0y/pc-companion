using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>
/// Decides which lock key changes show a notice, from the keys' state at each check, and whether
/// the Caps Lock badge shows. A change made where no notice could show, such as on the lock
/// screen, is taken without a notice with <see cref="Reset"/>. See "Behavior" and "Changes made
/// elsewhere" in docs/features/input/lock-keys.md. All calls must come from one thread.
/// </summary>
public sealed class LockKeyDetector
{
    // The order the notice lists the keys in.
    private static readonly LockKey[] Keys = [LockKey.CapsLock, LockKey.NumLock, LockKey.ScrollLock];

    private LockKeysSettings _settings = new();
    private LockKeyState? _state;

    /// <summary>The state at the last check or reset, or <see langword="null"/> before the first.</summary>
    public LockKeyState? State => _state;

    /// <summary>
    /// Whether the badge shows: <see cref="LockKeysSettings.KeepCapsLockBadge"/> is on, Caps Lock
    /// shows notices, and it's on, also when it was already on when the feature started.
    /// </summary>
    public bool ShowsBadge => _settings.KeepCapsLockBadge && _settings.CapsLock && _state is { CapsLock: true };

    /// <summary>Uses new settings. The state stays, so a change of the settings shows no notice.</summary>
    public void Configure(LockKeysSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
    }

    /// <summary>
    /// Takes <paramref name="state"/> as the current state without a notice: when the feature
    /// starts, and when the user comes back from the lock screen or another desktop.
    /// </summary>
    public void Reset(LockKeyState state) => _state = state;

    /// <summary>
    /// Takes the state read at a check, and returns the changes since the last check or reset that
    /// show a notice, Caps Lock first: changes of the keys that show notices, and of those only the
    /// ones turning on unless <see cref="LockKeysSettings.ShowWhenOff"/> is on. Presses between two
    /// checks that end where they started show nothing. The first state is taken as a reset.
    /// </summary>
    public IReadOnlyList<LockKeyChange> Check(LockKeyState state)
    {
        var previous = _state;
        _state = state;
        if (previous is not { } before || before == state)
        {
            return [];
        }

        var changes = new List<LockKeyChange>(Keys.Length);
        foreach (var key in Keys)
        {
            var on = state.IsOn(key);
            if (on != before.IsOn(key) && _settings.Shows(key) && (on || _settings.ShowWhenOff))
            {
                changes.Add(new LockKeyChange(key, on));
            }
        }

        return changes;
    }
}
