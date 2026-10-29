using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// An app with a window open, and the program file of its first window, which gives an app
/// without a name in the Windows Apps folder its name and icon.
/// </summary>
public sealed record RunningApp(ListedApp App, string? Program);

/// <summary>
/// The apps that have a window open, for the Running app dialog of the pages with a list of apps.
/// </summary>
public static class RunningApps
{
    /// <summary>
    /// Groups the windows that Alt + Tab shows, minimized ones too, into apps as the taskbar does
    /// (<see cref="AppOfWindow"/>): by the app ID a window sets, otherwise its Store package's app
    /// ID, otherwise its program file. The apps have no names yet, and the windows of the programs
    /// in <paramref name="leftOut"/>, such as PC Companion's, are left out.
    /// </summary>
    /// <param name="windows">The visible top-level windows, front to back.</param>
    public static IReadOnlyList<RunningApp> Of(IEnumerable<TopLevelWindow> windows, IAppIdentityReader reader, IReadOnlyCollection<string> leftOut)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(leftOut);

        var apps = new List<RunningApp>();
        var read = new AppOfWindowReader(reader);
        foreach (var window in windows.Where(IsAppWindow))
        {
            var app = read.Of(window);
            if (app.Program is { } path && leftOut.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var listed = app.AppId is { } appId ? new ListedApp { AppId = appId }
                : app.Program is { } program ? new ListedApp { Program = program }
                : null;
            if (listed is not null && !apps.Any(running => running.App.IsSameAppAs(listed)))
            {
                apps.Add(new RunningApp(listed, app.Program));
            }
        }

        return apps;
    }

    private static bool IsAppWindow(TopLevelWindow window) =>
        window.ProcessId != 0
        && !window.IsCloaked
        && !window.IsDesktop
        && (window.IsAppWindow || (!window.HasOwner && !window.IsToolWindow && !window.IsNoActivate));
}
