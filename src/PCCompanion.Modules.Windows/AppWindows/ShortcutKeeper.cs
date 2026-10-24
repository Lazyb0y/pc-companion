using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// Keeps the App Windows shortcut registered for the key above Tab, which means a different
/// virtual key in some keyboard layouts. Windows reports no layout change within a window, so
/// while the user's layouts disagree on that key, it also checks the layout once a second, and
/// soon after another window comes to the front, whose layout Windows may switch a moment later.
/// Use it on one thread; <paramref name="post"/> runs an action on that thread.
/// </summary>
public sealed partial class ShortcutKeeper(IShortcutEnvironment environment, TimeProvider time, Action<Action> post, ILogger logger) : IDisposable
{
    /// <summary>The scan code of the key above Tab, left of 1, on every keyboard.</summary>
    public const uint KeyAboveTab = 0x29;

    public static readonly TimeSpan LayoutCheckInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan FollowUpDelay = TimeSpan.FromMilliseconds(100);

    private HotKeyModifiers? _modifiers;
    private uint? _virtualKey;
    private IDisposable? _forward;
    private IDisposable? _back;
    private ITimer? _timer;
    private bool _attempted;
    private bool _disposed;

    /// <summary>Raised on the keeper's thread when the shortcut is pressed.</summary>
    public event EventHandler<SwitchDirection>? Pressed;

    /// <summary>Whether the layout is checked once a second, because the user's layouts disagree on the key above Tab.</summary>
    public bool ChecksLayout => _timer is not null;

    /// <summary>
    /// Registers the shortcut with <paramref name="modifiers"/>, and with Shift added to go back,
    /// or no shortcut for <see langword="null"/>.
    /// </summary>
    public void Configure(HotKeyModifiers? modifiers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Registers again, so a shortcut Windows refused before is tried again.
        _modifiers = modifiers;
        Unregister();
        CheckLayout();
    }

    /// <summary>
    /// Registers the shortcut again when the key above Tab means another virtual key in the layout
    /// of the window in front. Call it when another window comes to the front.
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
        Unregister();
        StopChecking();
    }

    private void CheckLayout()
    {
        if (_modifiers is not { } modifiers)
        {
            Unregister();
            StopChecking();
            return;
        }

        var layouts = environment.InstalledLayouts();
        if (environment.ForegroundLayout() is { } layout)
        {
            Register(modifiers, environment.VirtualKeyOf(KeyAboveTab, layout));
        }
        else if (!_attempted && layouts.Count > 0)
        {
            // Without a window in front, or in front of a console window, use the first layout.
            Register(modifiers, environment.VirtualKeyOf(KeyAboveTab, layouts[0]));
        }

        var keys = layouts.Select(installed => environment.VirtualKeyOf(KeyAboveTab, installed)).Distinct().Count();
        if (keys > 1)
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

    private void Register(HotKeyModifiers modifiers, uint? virtualKey)
    {
        // A refused shortcut isn't tried again until the key changes, so the log isn't flooded.
        if (_attempted && virtualKey == _virtualKey)
        {
            return;
        }

        Unregister();
        (_attempted, _virtualKey) = (true, virtualKey);
        if (virtualKey is not { } key)
        {
            LogNoKey(logger);
            return;
        }

        // Windows takes the two shortcuts one by one, so one it refuses leaves the other working.
        _forward = environment.Register(modifiers, key, () => Pressed?.Invoke(this, SwitchDirection.Forward));
        _back = environment.Register(modifiers | HotKeyModifiers.Shift, key, () => Pressed?.Invoke(this, SwitchDirection.Back));
        if (_forward is null)
        {
            LogRefused(logger, modifiers, key);
        }

        if (_back is null)
        {
            LogRefused(logger, modifiers | HotKeyModifiers.Shift, key);
        }

        if (_forward is not null && _back is not null)
        {
            LogRegistered(logger, modifiers, key);
        }
    }

    private void Unregister()
    {
        _forward?.Dispose();
        _back?.Dispose();
        (_forward, _back, _virtualKey, _attempted) = (null, null, null, false);
    }

    private void StopChecking()
    {
        _timer?.Dispose();
        _timer = null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "App Windows registered {Modifiers} with virtual key {VirtualKey:X2}.")]
    private static partial void LogRegistered(ILogger logger, HotKeyModifiers modifiers, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused the App Windows shortcut, {Modifiers} with virtual key {VirtualKey:X2}; another program may have it.")]
    private static partial void LogRefused(ILogger logger, HotKeyModifiers modifiers, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The key above Tab means no key in the layout in front, so App Windows has no shortcut there.")]
    private static partial void LogNoKey(ILogger logger);
}
