using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// A shortcut whose key is found by its position on the keyboard, the same on every layout.
/// </summary>
/// <param name="Modifiers">The modifier keys held with the key.</param>
/// <param name="ScanCode">The key's scan code, such as <see cref="ShortcutKeeper.KeyAboveTab"/>.</param>
public readonly record struct KeyPositionShortcut(HotKeyModifiers Modifiers, uint ScanCode);

/// <summary>
/// Keeps shortcuts registered for keys found by their position, such as the key above Tab, which
/// means a different virtual key in some keyboard layouts. Windows reports no layout change within
/// a window, so while the user's layouts disagree on one of the keys, it also checks the layout
/// once a second, and soon after another window comes to the front, whose layout Windows may
/// switch a moment later. Use it on one thread; <paramref name="post"/> runs an action on that thread.
/// </summary>
/// <param name="feature">The feature's name for the log, such as "App Windows".</param>
public sealed partial class ShortcutKeeper(IShortcutEnvironment environment, TimeProvider time, Action<Action> post, ILogger logger, string feature) : IDisposable
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

    /// <summary>Whether the layout is checked once a second, because the user's layouts disagree on one of the keys.</summary>
    public bool ChecksLayout => _timer is not null;

    /// <summary>Registers <paramref name="shortcuts"/>, one by one, in place of those registered before.</summary>
    public void Configure(IEnumerable<KeyPositionShortcut> shortcuts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Registers again, so a shortcut Windows refused before is tried again.
        UnregisterAll();
        foreach (var shortcut in shortcuts)
        {
            _registrations.TryAdd(shortcut, new Registration());
        }

        CheckLayout();
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

        CheckLayout();
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

        CheckLayout();
        _timer?.Change(LayoutCheckInterval, Timeout.InfiniteTimeSpan);
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
            LogNoKey(logger, feature, shortcut.Modifiers, shortcut.ScanCode);
            return;
        }

        // Windows takes the shortcuts one by one, so one it refuses leaves the others working.
        registration.Handle = environment.Register(shortcut.Modifiers, key, () => Pressed?.Invoke(this, shortcut));
        if (registration.Handle is null)
        {
            LogRefused(logger, feature, shortcut.Modifiers, key);
        }
        else
        {
            LogRegistered(logger, feature, shortcut.Modifiers, key);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Feature} registered {Modifiers} with virtual key {VirtualKey:X2}.")]
    private static partial void LogRegistered(ILogger logger, string feature, HotKeyModifiers modifiers, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused the {Feature} shortcut, {Modifiers} with virtual key {VirtualKey:X2}; another program may have it.")]
    private static partial void LogRefused(ILogger logger, string feature, HotKeyModifiers modifiers, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The key with scan code {ScanCode:X2} means no key in the layout in front, so {Feature} has no shortcut {Modifiers} with it there.")]
    private static partial void LogNoKey(ILogger logger, string feature, HotKeyModifiers modifiers, uint scanCode);

    // Where a shortcut is registered, and with which virtual key.
    private sealed class Registration
    {
        public bool Attempted { get; set; }

        public uint? VirtualKey { get; set; }

        public IDisposable? Handle { get; set; }

        public void Unregister()
        {
            Handle?.Dispose();
            (Handle, VirtualKey, Attempted) = (null, null, false);
        }
    }
}
