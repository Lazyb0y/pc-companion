using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The physical memory Windows can use and how much of it is available, in bytes, as
/// <c>GlobalMemoryStatusEx</c> reports it.
/// </summary>
public readonly record struct PhysicalMemory(ulong Total, ulong Available)
{
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static unsafe PhysicalMemory Read()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        if (!PInvoke.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception();
        }

        return new PhysicalMemory(status.ullTotalPhys, status.ullAvailPhys);
    }
}
