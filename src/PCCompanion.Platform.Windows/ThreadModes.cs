using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>How the current thread is scheduled.</summary>
public static class ThreadModes
{
    /// <summary>
    /// Runs the current thread at background priority, for its processor time and its disk access,
    /// so that the programs the user works with go first.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public static void EnterBackgroundMode()
    {
        if (!PInvoke.SetThreadPriority(PInvoke.GetCurrentThread(), THREAD_PRIORITY.THREAD_MODE_BACKGROUND_BEGIN))
        {
            throw new Win32Exception();
        }
    }
}
