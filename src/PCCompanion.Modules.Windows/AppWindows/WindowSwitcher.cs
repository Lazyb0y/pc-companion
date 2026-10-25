using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.AppWindows;

/// <summary>
/// Brings another window of the app in front to the front, as Command + ` does in macOS. See
/// docs/features/windows/app-windows.md.
/// </summary>
public sealed class WindowSwitcher(IAppWindowsEnvironment environment)
{
    /// <summary>
    /// Forward brings the app's window that is furthest back to the front, so repeated presses
    /// visit every window. Back brings the app's second window to the front and sends the one in
    /// front behind all windows, which undoes a press forward.
    /// </summary>
    public SwitchResult Switch(SwitchDirection direction, bool includeMinimized)
    {
        var windows = environment.ListWindows();
        var front = environment.ForegroundWindow;
        var root = front != 0 ? environment.RootOwner(front) : 0;
        var frontWindow = windows.FirstOrDefault(window => window.Handle == root);
        if (frontWindow is null || !IsSwitchable(frontWindow, includeMinimized: true))
        {
            return new SwitchResult(SwitchOutcome.NoAppInFront, 0);
        }

        var apps = new AppOfWindowReader(environment);
        var app = apps.Of(frontWindow);
        var others = windows
            .Where(window => window.Handle != frontWindow.Handle && IsSwitchable(window, includeMinimized))
            .Where(window => apps.Of(window).IsSameAppAs(app))
            .ToList();
        var count = others.Count + 1;
        if (others.Count == 0)
        {
            return new SwitchResult(SwitchOutcome.OneWindow, count);
        }

        if (direction == SwitchDirection.Forward)
        {
            return Activate(others[^1], count);
        }

        // The second window comes to the front first, so the old front window goes behind all
        // windows only when Windows allowed that; neither call waits for the windows' programs.
        // Always-on-top windows stay on top.
        var result = Activate(others[0], count);
        if (result.Outcome == SwitchOutcome.Switched && !frontWindow.IsTopmost)
        {
            environment.SendToBack(frontWindow.Handle);
        }

        return result;
    }

    /// <summary>
    /// Whether Alt + Tab would show the window: on this virtual desktop, not the desktop itself,
    /// and not a tool window, an owned window or one that can't take the keyboard focus, unless
    /// it asks to be on the taskbar.
    /// </summary>
    internal static bool IsSwitchable(TopLevelWindow window, bool includeMinimized) =>
        window.ProcessId != 0
        && !window.IsCloaked
        && !window.IsDesktop
        && (includeMinimized || !window.IsMinimized)
        && (window.IsAppWindow || (!window.HasOwner && !window.IsToolWindow && !window.IsNoActivate));

    private SwitchResult Activate(TopLevelWindow window, int count) =>
        new(environment.Activate(window.Handle) ? SwitchOutcome.Switched : SwitchOutcome.Refused, count);
}

public enum SwitchDirection
{
    Forward,
    Back,
}

public enum SwitchOutcome
{
    /// <summary>Another window of the app came to the front.</summary>
    Switched,

    /// <summary>Windows didn't bring the window to the front.</summary>
    Refused,

    /// <summary>The app in front has no other window to switch to.</summary>
    OneWindow,

    /// <summary>No app is in front, as when the desktop or the taskbar is.</summary>
    NoAppInFront,
}

/// <param name="WindowCount">How many windows the app in front has that count, including the front one.</param>
public sealed record SwitchResult(SwitchOutcome Outcome, int WindowCount);
