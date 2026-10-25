using System;
using System.Collections.Generic;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What a window's app is known by, as the taskbar tells apps apart: the app ID the window sets for
/// itself, the app ID of its process's Store app package, and its program file. Hot Corners and App
/// Windows both decide with it which app a window belongs to; <see cref="AppOfWindowReader"/>
/// reads it.
/// </summary>
/// <param name="WindowAppId">The app ID the window sets for itself, as browsers do for each profile.</param>
/// <param name="PackageAppId">The app ID of the process's Store app package.</param>
/// <param name="Program">The full path of the process's program file.</param>
/// <param name="ProcessId">The window's process, which tells apart windows without an app ID or a program.</param>
public sealed record AppOfWindow(string? WindowAppId, string? PackageAppId, string? Program, uint ProcessId)
{
    /// <summary>The app ID the taskbar groups the window by: the one it sets, otherwise its package's.</summary>
    public string? AppId => WindowAppId ?? PackageAppId;

    /// <summary>
    /// Whether two windows belong to the same app, as the taskbar groups them: the same app ID,
    /// whether a window sets it or its package has it; without app IDs, the same program; without
    /// either, the same process. App IDs and programs match whatever their case.
    /// </summary>
    public bool IsSameAppAs(AppOfWindow other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (AppId is not null || other.AppId is not null)
        {
            return string.Equals(AppId, other.AppId, StringComparison.OrdinalIgnoreCase);
        }

        if (Program is not null || other.Program is not null)
        {
            return string.Equals(Program, other.Program, StringComparison.OrdinalIgnoreCase);
        }

        return ProcessId == other.ProcessId;
    }

    /// <summary>Whether the window sets <paramref name="appId"/> or its package has it, whatever its case.</summary>
    public bool HasAppId(string appId) =>
        string.Equals(appId, WindowAppId, StringComparison.OrdinalIgnoreCase) || string.Equals(appId, PackageAppId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the window's program file is <paramref name="program"/>, whatever its case.</summary>
    public bool HasProgram(string program) => string.Equals(program, Program, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What tells apps apart, read from Windows or, in tests, supplied.</summary>
public interface IAppIdentityReader
{
    /// <summary>The app ID a window sets for itself, or <see langword="null"/>.</summary>
    string? WindowAppId(nint window);

    /// <summary>The app ID of a process's Store app package, or <see langword="null"/>.</summary>
    string? PackageAppId(uint processId);

    /// <summary>The full path of a process's program file, or <see langword="null"/> when it can't be read.</summary>
    string? ProgramPath(uint processId);
}

/// <summary>Reads what tells apps apart from Windows, with <see cref="AppIdentity"/>. None of it waits for the app.</summary>
public sealed class WindowsAppIdentityReader : IAppIdentityReader
{
    public string? WindowAppId(nint window) => AppIdentity.OfWindow(window);

    public string? PackageAppId(uint processId) => AppIdentity.OfPackagedProcess(processId);

    public string? ProgramPath(uint processId) => AppIdentity.ProgramPath(processId);
}

/// <summary>
/// Reads the app of each window for one look at the windows, such as one press of a shortcut,
/// reading each process's package and program once. Use it on one thread.
/// </summary>
public sealed class AppOfWindowReader(IAppIdentityReader reader)
{
    private readonly Dictionary<uint, (string? Package, string? Program)> _processes = [];

    public AppOfWindow Of(TopLevelWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!_processes.TryGetValue(window.ProcessId, out var process))
        {
            process = (reader.PackageAppId(window.ProcessId), reader.ProgramPath(window.ProcessId));
            _processes[window.ProcessId] = process;
        }

        return new AppOfWindow(reader.WindowAppId(window.Handle), process.Package, process.Program, window.ProcessId);
    }
}
