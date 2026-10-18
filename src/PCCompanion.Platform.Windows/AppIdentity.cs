using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// What tells apps apart, as the taskbar does: the app ID (AppUserModelID) a window sets for
/// itself, a Store app's package, or the program file. The user's programs can read them for each
/// other's windows and processes without admin rights.
/// </summary>
public static class AppIdentity
{
    // APPLICATION_USER_MODEL_ID_MAX_LENGTH, with the terminating null.
    private const int MaximumAppIdLength = 130;

    // Paths may be longer than MAX_PATH; the second size fits any.
    private static readonly int[] PathBufferSizes = [1024, 32768];

    /// <summary>
    /// The app ID <paramref name="window"/> sets for itself, as browsers do for each profile and
    /// Store apps do on their frame window, or <see langword="null"/> when it sets none or it can't
    /// be read.
    /// </summary>
    public static unsafe string? OfWindow(nint window)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (PInvoke.SHGetPropertyStoreForWindow(new HWND(window), &iid, out var result).Failed || result is not IPropertyStore store)
        {
            return null;
        }

        try
        {
            var key = PInvoke.PKEY_AppUserModel_ID;
            store.GetValue(&key, out var value);
            try
            {
                var id = value.vt == VARENUM.VT_LPWSTR ? value.pwszVal.ToString() : null;
                return string.IsNullOrEmpty(id) ? null : id;
            }
            finally
            {
                PInvoke.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>
    /// The app ID of the Store app package that the process with ID <paramref name="processId"/>
    /// belongs to, or <see langword="null"/> when it isn't one or it can't be read.
    /// </summary>
    public static string? OfPackagedProcess(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid)
        {
            return null;
        }

        Span<char> id = stackalloc char[MaximumAppIdLength];
        var length = (uint)id.Length;
        return PInvoke.GetApplicationUserModelId(process, ref length, id) == WIN32_ERROR.ERROR_SUCCESS && length > 1
            ? new string(id[..((int)length - 1)])
            : null;
    }

    /// <summary>
    /// The full path of the program file of the process with ID <paramref name="processId"/>, or
    /// <see langword="null"/> when it can't be read, for example because the process has ended.
    /// </summary>
    public static string? ProgramPath(uint processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid)
        {
            return null;
        }

        foreach (var size in PathBufferSizes)
        {
            var path = new char[size];
            var length = (uint)size;
            if (PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref length))
            {
                return new string(path, 0, (int)length);
            }

            if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                return null;
            }
        }

        return null;
    }
}
