using System;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.LockKeys;

/// <summary>Which lock keys are on, as one check reads them.</summary>
public readonly record struct LockKeyState(bool CapsLock, bool NumLock, bool ScrollLock)
{
    public bool IsOn(LockKey key) => key switch
    {
        LockKey.CapsLock => CapsLock,
        LockKey.NumLock => NumLock,
        LockKey.ScrollLock => ScrollLock,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}

/// <summary>A lock key that turned on or off since the last check.</summary>
public readonly record struct LockKeyChange(LockKey Key, bool IsOn);
