using System;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Removes another program's window's taskbar button, which on Windows 11 also takes it out of
/// Alt + Tab, and adds it back (<c>ITaskbarList</c>). The window stays as it is. Explorer adds the
/// button again when it restarts. It asks Explorer, so don't call it on a thread that must not wait.
/// Windows reports success for a window of an app running as administrator while this process
/// doesn't, and leaves its button.
/// </summary>
public static class TaskbarButtons
{
    /// <summary>Removes <paramref name="window"/>'s taskbar button. Returns false when Windows refused.</summary>
    public static bool Remove(nint window) => Call(list => list.DeleteTab(new HWND(window)));

    /// <summary>Adds <paramref name="window"/>'s taskbar button back. Returns false when Windows refused.</summary>
    public static bool Add(nint window) => Call(list => list.AddTab(new HWND(window)));

    private static bool Call(Action<ITaskbarList> call)
    {
        try
        {
            var list = (ITaskbarList)new TaskbarList();
            try
            {
                list.HrInit();
                call(list);
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(list);
            }
        }
        catch (COMException)
        {
            return false;
        }
    }
}
