using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// End App's rules for choosing an app: the window under the pointer, the windows Tab visits,
/// which process a window stands for, and why an app can't be ended. See "Choosing an app" and
/// "Which processes end" in docs/features/windows/end-app.md. Use it on the feature's thread.
/// </summary>
public sealed class AppChooser(IEndAppSystem system)
{
    private const string StandInClass = "Ghost";
    private const string SharedFrameClass = "ApplicationFrameWindow";

    // Windows' own processes behind the desktop, by program file in the Windows folder.
    private static readonly FrozenSet<string> WindowsPrograms = new[]
    {
        "dwm.exe", "ApplicationFrameHost.exe", "conhost.exe", "sihost.exe", "ctfmon.exe", "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe", "TextInputHost.exe", "LockApp.exe", "taskhostw.exe",
        "RuntimeBroker.exe", "svchost.exe", "fontdrvhost.exe", "csrss.exe", "winlogon.exe", "wininit.exe", "services.exe",
        "lsass.exe", "smss.exe",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Why each process can't be ended, while the user chooses: reading it opens the process.
    private readonly Dictionary<uint, EndRefusal> _refusals = [];

    /// <summary>
    /// The front-most window whose visible frame holds <paramref name="point"/>, skipping PC
    /// Companion's own windows, windows on other virtual desktops and windows that let clicks
    /// through; 0 when there's none.
    /// </summary>
    public nint WindowAt(Point point, IReadOnlyList<EndAppWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        foreach (var window in windows)
        {
            if (IsCandidate(window) && !window.IsClickThrough && window.VisibleBounds.Contains(point))
            {
                return window.Handle;
            }
        }

        return 0;
    }

    /// <summary>
    /// The windows Tab and Shift + Tab visit, front to back: those the taskbar shows, without a
    /// window that a stand-in of Windows' stands for, since the stand-in is visited instead.
    /// </summary>
    public IReadOnlyList<nint> TabOrder(IReadOnlyList<EndAppWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var standIns = windows.Where(window => window.IsStandIn).Select(window => window.Bounds).ToHashSet();
        return windows
            .Where(window => IsCandidate(window) && window.MayHaveTaskbarButton && (window.IsStandIn || !standIns.Contains(window.Bounds)))
            .Select(window => window.Handle)
            .ToList();
    }

    /// <summary>
    /// What choosing <paramref name="window"/> means: its main window, which the frame goes
    /// around, the window and process of the app to end, and why it can't be ended, if it can't.
    /// </summary>
    public ChosenApp Describe(nint window)
    {
        if (window == 0)
        {
            return ChosenApp.Nothing;
        }

        var root = system.RootOwner(window);
        if (root == 0)
        {
            root = window;
        }

        var app = root;
        switch (system.ClassName(root))
        {
            case StandInClass:
                var hung = system.NotRespondingWindowsAt(system.WindowRect(root));
                if (hung.Count != 1)
                {
                    return new ChosenApp(root, 0, 0, EndRefusal.CantTellWhich);
                }

                app = hung[0];
                break;
            case SharedFrameClass:
                app = system.CoreWindowOf(root);
                if (app == 0)
                {
                    return new ChosenApp(root, 0, 0, EndRefusal.CantTellWhich);
                }

                break;
        }

        var processId = system.ProcessIdOf(app);
        return new ChosenApp(root, app, processId, RefusalFor(processId));
    }

    /// <summary>Forgets why processes couldn't be ended, as the user starts choosing again.</summary>
    public void Forget() => _refusals.Clear();

    /// <summary>
    /// Why the process with ID <paramref name="processId"/> can't be ended, or
    /// <see cref="EndRefusal.None"/>. It opens the process to see what Windows allows.
    /// </summary>
    public EndRefusal RefusalFor(uint processId)
    {
        if (!_refusals.TryGetValue(processId, out var refusal))
        {
            refusal = ReadRefusal(processId);
            _refusals[processId] = refusal;
        }

        return refusal;
    }

    /// <summary>
    /// Why a process's program file makes it one that never ends: File Explorer and Windows' own
    /// processes behind the desktop, in the Windows folder; <see cref="EndRefusal.None"/> otherwise.
    /// </summary>
    public static EndRefusal RefusalForProgram(string? programPath, string windowsFolder)
    {
        ArgumentNullException.ThrowIfNull(windowsFolder);

        if (programPath is null || !programPath.StartsWith(windowsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return EndRefusal.None;
        }

        var name = Path.GetFileName(programPath);
        return string.Equals(name, "explorer.exe", StringComparison.OrdinalIgnoreCase) ? EndRefusal.FileExplorer
            : WindowsPrograms.Contains(name) ? EndRefusal.PartOfWindows
            : EndRefusal.None;
    }

    /// <summary>
    /// The processes the app's process started that run its program file, and theirs, such as a
    /// browser's helpers. A process counts when its parent's ID is the app's, or a helper's, and it
    /// started after its parent, so a process ID Windows gave out again doesn't. Lists the processes,
    /// so call it on the thread pool.
    /// </summary>
    public static IReadOnlyList<uint> Helpers(IEndAppSystem system, uint processId, DateTime? startTime, string? programPath)
    {
        ArgumentNullException.ThrowIfNull(system);

        var helpers = new List<uint>();
        if (startTime is not { } appStart || programPath is null)
        {
            return helpers;
        }

        var processes = system.ListProcesses();
        var parents = new Queue<(uint Id, DateTime Start)>([(processId, appStart)]);
        while (parents.TryDequeue(out var parent))
        {
            foreach (var process in processes)
            {
                if (process.ParentId != parent.Id || process.Id == processId || helpers.Contains(process.Id))
                {
                    continue;
                }

                if (system.StartTime(process.Id) is { } start && start >= parent.Start
                    && string.Equals(system.ProgramPath(process.Id), programPath, StringComparison.OrdinalIgnoreCase))
                {
                    helpers.Add(process.Id);
                    parents.Enqueue((process.Id, start));
                }
            }
        }

        return helpers;
    }

    private bool IsCandidate(EndAppWindow window) => window.ProcessId != system.OwnProcessId && !window.IsCloaked;

    private EndRefusal ReadRefusal(uint processId)
    {
        if (processId == 0)
        {
            return EndRefusal.CantTellWhich;
        }

        if (processId == system.OwnProcessId)
        {
            return EndRefusal.PartOfPcCompanion;
        }

        if (RefusalForProgram(system.ProgramPath(processId), system.WindowsFolder) is not EndRefusal.None and var byProgram)
        {
            return byProgram;
        }

        using var process = system.Open(processId, out _);
        if (process is null)
        {
            return system.IsElevated(processId) == true ? EndRefusal.RunsAsAdministrator : EndRefusal.NotAllowed;
        }

        return process.IsCritical == true ? EndRefusal.PartOfWindows : EndRefusal.None;
    }
}

/// <summary>What choosing a window means.</summary>
/// <param name="Window">The main window the frame goes around, or 0 for none.</param>
/// <param name="AppWindow">
/// The window whose process ends and whose state the question follows: the window that stopped
/// responding for Windows' stand-in, a Store app's core window for the shared frame, otherwise
/// <paramref name="Window"/>; 0 when there's none.
/// </param>
/// <param name="ProcessId">The process to end, or 0 for none.</param>
/// <param name="Refusal">Why the app can't be ended, or <see cref="EndRefusal.None"/>.</param>
public sealed record ChosenApp(nint Window, nint AppWindow, uint ProcessId, EndRefusal Refusal)
{
    /// <summary>No window: nothing to frame or end.</summary>
    public static ChosenApp Nothing { get; } = new(0, 0, 0, EndRefusal.CantTellWhich);

    public bool CanEnd => Refusal == EndRefusal.None;
}

/// <summary>Why an app can't be ended.</summary>
public enum EndRefusal
{
    /// <summary>It can be ended.</summary>
    None,

    /// <summary>File Explorer, which also runs the desktop and the taskbar.</summary>
    FileExplorer,

    /// <summary>One of Windows' own processes, or one Windows marks critical.</summary>
    PartOfWindows,

    /// <summary>PC Companion's host.</summary>
    PartOfPcCompanion,

    /// <summary>It runs as administrator while PC Companion doesn't.</summary>
    RunsAsAdministrator,

    /// <summary>Windows doesn't let PC Companion end it, as another user's or a protected process.</summary>
    NotAllowed,

    /// <summary>
    /// End App can't tell which app it is: a stand-in for none or several windows that stopped
    /// responding, a shared frame without its app, or no window.
    /// </summary>
    CantTellWhich,
}
