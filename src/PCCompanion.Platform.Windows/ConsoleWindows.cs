using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Console windows, such as Command Prompt's, which the console host process (<c>conhost.exe</c>)
/// runs for a console program. Windows reports the console program as the window's process, and
/// that program's thread has no keyboard layout: the console's layout is on the console host's
/// thread that owns the window.
/// </summary>
public static class ConsoleWindows
{
    private const string ClassName = "ConsoleWindowClass";

    /// <summary>Whether <paramref name="window"/> is a console window.</summary>
    public static bool IsConsole(nint window) => TopLevelWindows.ClassName(window) == ClassName;

    /// <summary>
    /// The console host's thread that owns <paramref name="window"/>, a console window whose
    /// process Windows reports as <paramref name="processId"/>, or <see langword="null"/> when it
    /// can't be found. The console host is a child process of the console program. Lists the
    /// processes and threads, about 25 ms, so call it off the UI thread.
    /// </summary>
    public static uint? HostThreadOf(nint window, uint processId)
    {
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS | CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD, 0);
        if (snapshot.IsInvalid)
        {
            return null;
        }

        var children = new HashSet<uint>();
        var process = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
        for (var more = PInvoke.Process32FirstW(snapshot, ref process); more; more = PInvoke.Process32NextW(snapshot, ref process))
        {
            if (process.th32ParentProcessID == processId && process.th32ProcessID != processId)
            {
                children.Add(process.th32ProcessID);
            }
        }

        if (children.Count == 0)
        {
            return null;
        }

        var console = new HWND(window);
        var thread = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
        for (var more = PInvoke.Thread32First(snapshot, ref thread); more; more = PInvoke.Thread32Next(snapshot, ref thread))
        {
            if (children.Contains(thread.th32OwnerProcessID) && OwnsWindow(thread.th32ThreadID, console))
            {
                return thread.th32ThreadID;
            }
        }

        return null;
    }

    private static bool OwnsWindow(uint thread, HWND window)
    {
        var found = false;
        PInvoke.EnumThreadWindows(thread, (candidate, _) =>
        {
            found = candidate == window;
            return !found;
        }, 0);
        return found;
    }
}
