using System;

namespace PCCompanion.Modules.Windows;

/// <summary>
/// Carries windows from Title Bar Actions to Minimize to Tray, Hide Apps and Window Tabs, as "With
/// the other features" in docs/features/windows/title-bar-actions.md says. While one of those
/// features runs, it sets how it takes a window, which goes on to its thread. Use it on the host's
/// UI thread.
/// </summary>
public sealed class WindowRequests
{
    private Func<nint, bool>? _sendToTray;
    private Func<nint, bool>? _hideApp;
    private Func<nint, bool>? _addToGroupBehind;
    private Func<nint, bool>? _takeOutOfGroup;

    /// <summary>Sends <paramref name="window"/> to the tray. Returns false while Minimize to Tray doesn't run.</summary>
    public bool SendToTray(nint window) => _sendToTray?.Invoke(window) ?? false;

    /// <summary>Minimizes every window of <paramref name="window"/>'s app. Returns false while Hide Apps doesn't run.</summary>
    public bool HideApp(nint window) => _hideApp?.Invoke(window) ?? false;

    /// <summary>Adds <paramref name="window"/> to the group of the window behind it. Returns false while Window Tabs doesn't run.</summary>
    public bool AddToGroupBehind(nint window) => _addToGroupBehind?.Invoke(window) ?? false;

    /// <summary>Takes <paramref name="window"/> out of its group. Returns false while Window Tabs doesn't run.</summary>
    public bool TakeOutOfGroup(nint window) => _takeOutOfGroup?.Invoke(window) ?? false;

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
