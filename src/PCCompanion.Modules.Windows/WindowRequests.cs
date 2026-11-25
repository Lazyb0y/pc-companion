using System;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// Carries windows from Title Bar Actions to Minimize to Tray, Hide Apps and Window Tabs, as "With
/// the other features" in docs/features/windows/title-bar-actions.md says, and from Window Rules
/// to Window Tabs. While one of those features runs, it sets how it takes a window, which goes on
/// to its thread. Use it on the host's UI thread, except <see cref="GroupWith"/>.
/// </summary>
public sealed class WindowRequests
{
    private Func<nint, bool>? _sendToTray;
    private Func<nint, bool>? _hideApp;
    private Func<nint, bool>? _addToGroupBehind;
    private Func<nint, bool>? _takeOutOfGroup;

    // Read on Window Rules' thread.
    private volatile Func<nint, nint, bool>? _groupWith;

    // Read on Title Bar Actions' thread.
    private volatile Action<nint, int>? _opacityChanged;

    /// <summary>Sends <paramref name="window"/> to the tray. Returns false while Minimize to Tray doesn't run.</summary>
    public bool SendToTray(nint window) => _sendToTray?.Invoke(window) ?? false;

    /// <summary>Minimizes every window of <paramref name="window"/>'s app. Returns false while Hide Apps doesn't run.</summary>
    public bool HideApp(nint window) => _hideApp?.Invoke(window) ?? false;

    /// <summary>Adds <paramref name="window"/> to the group of the window behind it. Returns false while Window Tabs doesn't run.</summary>
    public bool AddToGroupBehind(nint window) => _addToGroupBehind?.Invoke(window) ?? false;

    /// <summary>Takes <paramref name="window"/> out of its group. Returns false while Window Tabs doesn't run.</summary>
    public bool TakeOutOfGroup(nint window) => _takeOutOfGroup?.Invoke(window) ?? false;

    /// <summary>
    /// Adds <paramref name="window"/> to the group of <paramref name="target"/>, or starts a group
    /// of the two, without a sound, as Window Rules' Open as a tab of does. Returns false while
    /// Window Tabs doesn't run. Any thread can call it.
    /// </summary>
    public bool GroupWith(nint window, nint target) => _groupWith?.Invoke(window, target) ?? false;

    /// <summary>Window Tabs sets how it groups a window with another as it starts, and null as it stops.</summary>
    public void SetGroupWith(Func<nint, nint, bool>? groupWith) => _groupWith = groupWith;

    /// <summary>
    /// Tells Window Rules that Title Bar Actions' wheel gave <paramref name="window"/> the opacity
    /// <paramref name="percent"/>, for rules that remember it. Any thread can call it.
    /// </summary>
    public void OpacityChanged(nint window, int percent) => _opacityChanged?.Invoke(window, percent);

    /// <summary>Window Rules sets how it hears of the wheel's opacity as it starts, and null as it stops.</summary>
    public void SetOpacityChanged(Action<nint, int>? opacityChanged) => _opacityChanged = opacityChanged;

    /// <summary>Minimize to Tray sets how it takes a window as it starts, and null as it stops.</summary>
    public void SetTray(Func<nint, bool>? sendToTray) => _sendToTray = sendToTray;

    /// <summary>Hide Apps sets how it takes a window as it starts, and null as it stops.</summary>
    public void SetHideApps(Func<nint, bool>? hideApp) => _hideApp = hideApp;

    /// <summary>Window Tabs sets how it takes a window as it starts, and nulls as it stops.</summary>
    public void SetWindowTabs(Func<nint, bool>? addToGroupBehind, Func<nint, bool>? takeOutOfGroup)
    {
        _addToGroupBehind = addToGroupBehind;
        _takeOutOfGroup = takeOutOfGroup;
    }
}
