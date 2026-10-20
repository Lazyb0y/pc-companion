using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// How long the processors have been idle, in kernel mode and in user mode since Windows started,
/// in 100-nanosecond units, summed over the processors, as <c>GetSystemTimes</c> reports it.
/// Kernel time includes idle time. On PCs with more than 64 logical processors, it covers only
/// the processor group of the calling thread.
/// </summary>
public readonly record struct ProcessorTimes(long Idle, long Kernel, long User)
{
    /// <summary>Returns false when Windows refused.</summary>
    public static bool TryRead(out ProcessorTimes times)
    {
        if (!PInvoke.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            times = default;
            return false;
        }

        times = new ProcessorTimes(ToTicks(idle), ToTicks(kernel), ToTicks(user));
        return true;
    }

    private static long ToTicks(FILETIME time) => ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
}
