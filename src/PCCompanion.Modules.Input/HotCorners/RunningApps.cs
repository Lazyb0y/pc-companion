using System;
using System.Collections.Generic;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// What tells apps apart, read from Windows or, in tests, supplied.
/// </summary>
public interface IAppIdentityReader
{
    /// <summary>The app ID a window sets for itself, or <see langword="null"/>.</summary>
    string? WindowAppId(nint window);

    /// <summary>The app ID of a process's Store app package, or <see langword="null"/>.</summary>
    string? PackageAppId(uint processId);

    /// <summary>The full path of a process's program file, or <see langword="null"/>.</summary>
    string? ProgramPath(uint processId);
}

/// <summary>
/// Reads what tells apps apart from Windows. None of it waits for the app.
/// </summary>
public sealed class WindowsAppIdentityReader : IAppIdentityReader
{
    public string? WindowAppId(nint window) => AppIdentity.OfWindow(window);

    public string? PackageAppId(uint processId) => AppIdentity.OfPackagedProcess(processId);

    public string? ProgramPath(uint processId) => AppIdentity.ProgramPath(processId);
}

/// <summary>
/// An app with a window open, and the program file of its first window, which gives an app
/// without a name in the Windows Apps folder its name and icon.
/// </summary>
public sealed record RunningApp(ExcludedApp App, string? Program);

/// <summary>
/// The apps that have a window open, for the page's Running app dialog. See "Apps that turn
/// corners off" in docs/features/input/hot-corners.md.
/// </summary>
public static class RunningApps
{
    /// <summary>
    /// Groups the windows that Alt + Tab shows, minimized ones too, into apps as the taskbar does:
    /// by the app ID a window sets, otherwise its Store package's app ID, otherwise its program
    /// file. The apps have no names yet, and the windows of the programs in
    /// <paramref name="leftOut"/>, such as PC Companion's, are left out.
    /// </summary>
    /// <param name="windows">The visible top-level windows, front to back.</param>
    public static IReadOnlyList<RunningApp> Of(IEnumerable<TopLevelWindow> windows, IAppIdentityReader reader, IReadOnlyCollection<string> leftOut)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(leftOut);

        var apps = new List<RunningApp>();
        var processes = new Dictionary<uint, (string? Package, string? Program)>();
        foreach (var window in windows.Where(IsAppWindow))
        {
            if (!processes.TryGetValue(window.ProcessId, out var process))
            {
                process = (reader.PackageAppId(window.ProcessId), reader.ProgramPath(window.ProcessId));
                processes[window.ProcessId] = process;
            }

            if (process.Program is { } path && leftOut.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var app = reader.WindowAppId(window.Handle) is { } windowAppId ? new ExcludedApp { AppId = windowAppId }
                : process.Package is { } packageAppId ? new ExcludedApp { AppId = packageAppId }
                : process.Program is { } program ? new ExcludedApp { Program = program }
                : null;
            if (app is not null && !apps.Any(listed => listed.App.IsSameAppAs(app)))
            {
                apps.Add(new RunningApp(app, process.Program));
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
