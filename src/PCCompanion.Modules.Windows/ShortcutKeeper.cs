using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// A shortcut whose key is found by its position on the keyboard, the same on every layout.
/// </summary>
/// <param name="Modifiers">The modifier keys held with the key.</param>
/// <param name="ScanCode">The key's scan code, such as <see cref="ShortcutKeeper.KeyAboveTab"/>.</param>
public readonly record struct KeyPositionShortcut(HotKeyModifiers Modifiers, uint ScanCode);

/// <summary>A shortcut and the virtual key its key means in the layout it was registered for.</summary>
public readonly record struct RegisteredKey(KeyPositionShortcut Shortcut, uint VirtualKey);

/// <summary>
/// Keeps shortcuts registered for keys found by their position, such as the key above Tab, which
/// means a different virtual key in some keyboard layouts. Windows reports no layout change within
/// a window, so while the user's layouts disagree on one of the keys, it also checks the layout
/// once a second, and soon after another window comes to the front, whose layout Windows may
/// switch a moment later. Use it on one thread; <paramref name="post"/> runs an action on that thread.
/// It logs nothing itself: its events say what it registered, what Windows refused and which key
/// a layout doesn't have.
/// </summary>
public sealed class ShortcutKeeper(IShortcutEnvironment environment, TimeProvider time, Action<Action> post) : IDisposable
{
    /// <summary>The scan code of the key above Tab, left of 1, on every keyboard.</summary>
    public const uint KeyAboveTab = 0x29;

    public static readonly TimeSpan LayoutCheckInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<KeyPositionShortcut, Registration> _registrations = [];
    private ITimer? _timer;
    private bool _disposed;

    /// <summary>Raised on the keeper's thread when one of the shortcuts is pressed.</summary>
    public event EventHandler<KeyPositionShortcut>? Pressed;

    /// <summary>Raised on the keeper's thread when one of the shortcuts is registered with a virtual key.</summary>
    public event EventHandler<RegisteredKey>? Registered;

    /// <summary>Raised on the keeper's thread when Windows refuses one of the shortcuts, once for each key it's tried with.</summary>
    public event EventHandler<RegisteredKey>? Refused;

    /// <summary>Raised on the keeper's thread when a shortcut's key means no virtual key in the layout in front.</summary>
    public event EventHandler<KeyPositionShortcut>? NoKey;

    /// <summary>Raised on the keeper's thread when <see cref="RefusedShortcuts"/> changed.</summary>
    public event EventHandler? RefusedShortcutsChanged;

    /// <summary>The shortcuts that Windows refused the last time they were registered.</summary>
    public IReadOnlyList<KeyPositionShortcut> RefusedShortcuts => [.. _registrations.Where(pair => pair.Value.Refused).Select(pair => pair.Key)];

    /// <summary>Whether the layout is checked once a second, because the user's layouts disagree on one of the keys.</summary>
    public bool ChecksLayout => _timer is not null;

    /// <summary>Registers <paramref name="shortcuts"/>, one by one, in place of those registered before.</summary>
    public void Configure(IEnumerable<KeyPositionShortcut> shortcuts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Registers again, so a shortcut Windows refused before is tried again.
        Tracked(() =>
        {
            UnregisterAll();
            foreach (var shortcut in shortcuts)
            {
                _registrations.TryAdd(shortcut, new Registration());
            }

            CheckLayout();
        });
    }

    /// <summary>
    /// Registers a shortcut again when its key means another virtual key in the layout of the
    /// window in front. Call it when another window comes to the front.
    /// </summary>
    public void Check()
    {
        if (_disposed)
        {
            return;
        }

        Tracked(CheckLayout);
        _timer?.Change(FollowUpDelay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _disposed = true;
        UnregisterAll();
        StopChecking();
    }

    private void CheckLayout()
    {
        if (_registrations.Count == 0)
        {
            StopChecking();
            return;
        }

        var layouts = environment.InstalledLayouts();
        var foreground = environment.ForegroundLayout();
        foreach (var (shortcut, registration) in _registrations)
        {
            if (foreground is { } layout)
            {
                Register(shortcut, registration, environment.VirtualKeyOf(shortcut.ScanCode, layout));
            }
            else if (!registration.Attempted && layouts.Count > 0)
            {
                // Without a window in front, or in front of a console window, use the first layout.
                Register(shortcut, registration, environment.VirtualKeyOf(shortcut.ScanCode, layouts[0]));
            }
        }

        var disagree = _registrations.Keys
            .Select(shortcut => shortcut.ScanCode)
            .Distinct()
            .Any(scanCode => layouts.Select(installed => environment.VirtualKeyOf(scanCode, installed)).Distinct().Count() > 1);
        if (disagree)
        {
            _timer ??= time.CreateTimer(_ => post(OnTimer), null, LayoutCheckInterval, Timeout.InfiniteTimeSpan);
        }
        else
        {
            StopChecking();
        }
    }

    private void OnTimer()
    {
        if (_disposed || _timer is null)
        {
            return;
        }

        Tracked(CheckLayout);
        _timer?.Change(LayoutCheckInterval, Timeout.InfiniteTimeSpan);
    }

    // Runs a change, and says so when it changed which shortcuts Windows refused.
    private void Tracked(Action change)
    {
        var before = RefusedShortcuts;
        change();
        if (!before.ToHashSet().SetEquals(RefusedShortcuts))
        {
            RefusedShortcutsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Register(KeyPositionShortcut shortcut, Registration registration, uint? virtualKey)
    {
        // A refused shortcut isn't tried again until its key changes, so the log isn't flooded.
        if (registration.Attempted && virtualKey == registration.VirtualKey)
        {
            return;
        }

        registration.Unregister();
        (registration.Attempted, registration.VirtualKey) = (true, virtualKey);
        if (virtualKey is not { } key)
        {
            NoKey?.Invoke(this, shortcut);
            return;
        }

        // Windows takes the shortcuts one by one, so one it refuses leaves the others working.
        registration.Handle = environment.Register(shortcut.Modifiers, key, () => Pressed?.Invoke(this, shortcut));
        if (registration.Handle is null)
        {
            registration.Refused = true;
            Refused?.Invoke(this, new RegisteredKey(shortcut, key));
        }
        else
        {
            Registered?.Invoke(this, new RegisteredKey(shortcut, key));
        }
    }

    private void UnregisterAll()
    {
        foreach (var registration in _registrations.Values)
        {
            registration.Unregister();
        }

        _registrations.Clear();
    }

    private void StopChecking()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // Where a shortcut is registered, and with which virtual key.
    private sealed class Registration
    {
        public bool Attempted { get; set; }

        public uint? VirtualKey { get; set; }

        public IDisposable? Handle { get; set; }

        public bool Refused { get; set; }

        public void Unregister()
        {
            Handle?.Dispose();
            (Handle, VirtualKey, Attempted, Refused) = (null, null, false, false);
        }
    }
}
