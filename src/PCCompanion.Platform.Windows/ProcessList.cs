using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The processes running in Windows, with each one's parent, and when a process started. Listing
/// them takes a few tens of milliseconds, so call it off the UI thread.
/// </summary>
public static class ProcessList
{
    /// <summary>
    /// Every running process with the ID of the process that started it. A parent may have ended,
    /// and Windows may have given its ID to another process since, so compare start times.
    /// </summary>
    public static IReadOnlyList<ProcessEntry> List()
    {
        var result = new List<ProcessEntry>();
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            return result;
        }

        var process = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
        for (var more = PInvoke.Process32FirstW(snapshot, ref process); more; more = PInvoke.Process32NextW(snapshot, ref process))
        {
            result.Add(new ProcessEntry(process.th32ProcessID, process.th32ParentProcessID));
        }

        return result;
    }

    /// <summary>
    /// When the process with ID <paramref name="processId"/> started, in UTC, or <see langword="null"/>
    /// when it can't be read, for example because it has ended.
    /// </summary>
    public static DateTime? StartTime(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        return process.IsInvalid ? null : StartTime(process);
    }

    /// <summary>When a process opened with at least <c>PROCESS_QUERY_LIMITED_INFORMATION</c> started, in UTC.</summary>
    internal static DateTime? StartTime(SafeHandle process) =>
        PInvoke.GetProcessTimes(process, out var creation, out _, out _, out _)
            ? DateTime.FromFileTimeUtc(ToTicks(creation))
            : null;

    private static long ToTicks(FILETIME time) => ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
}

/// <summary>A running process and the ID of the process that started it.</summary>
public readonly record struct ProcessEntry(uint Id, uint ParentId);
