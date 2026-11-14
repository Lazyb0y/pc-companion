using System;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.CloudFilters;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// A file in a sync root opened with <c>CfOpenFileWithOplock</c>: Windows closes its handle when a
/// program opens the file, so holding it never blocks the user's programs. The Win32 handle stays
/// Windows': it's used only while the protected handle is referenced, so Windows doesn't close it
/// meanwhile, and it's never closed here.
/// </summary>
internal sealed class OplockHandle : IDisposable
{
    /// <summary>HRESULT_FROM_WIN32(ERROR_OPLOCK_HANDLE_CLOSED): a program opened the file, so Windows closed the handle.</summary>
    public const int Closed = unchecked((int)0x80070323);

    private readonly CfCloseHandleSafeHandle _protected;
    private readonly SafeFileHandle _win32;

    private OplockHandle(CfCloseHandleSafeHandle protectedHandle, SafeFileHandle win32)
    {
        _protected = protectedHandle;
        _win32 = win32;
    }

    /// <summary>
    /// Opens <paramref name="path"/>; with <see cref="CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE"/>,
    /// it fails with a sharing violation while a program has the file open.
    /// </summary>
    /// <returns>Windows' HRESULT, 0 when <paramref name="handle"/> is open.</returns>
    public static int Open(string path, CF_OPEN_FILE_FLAGS flags, out OplockHandle? handle)
    {
        handle = null;
        int result = PInvoke.CfOpenFileWithOplock(path, flags, out var protectedHandle).Value;
        if (result != 0)
        {
            protectedHandle.Dispose();
            return result;
        }

        if (!PInvoke.CfReferenceProtectedHandle(protectedHandle))
        {
            protectedHandle.Dispose();
            return Closed;
        }

        try
        {
            HANDLE win32 = PInvoke.CfGetWin32HandleFromProtectedHandle((HANDLE)protectedHandle.DangerousGetHandle());
            handle = new OplockHandle(protectedHandle, new SafeFileHandle((nint)win32, ownsHandle: false));
            return 0;
        }
        finally
        {
            PInvoke.CfReleaseProtectedHandle(protectedHandle);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the Win32 handle while Windows keeps it open, and returns
    /// its HRESULT, or <see cref="Closed"/> when a program opened the file and Windows closed it.
    /// </summary>
    public int Use(Func<SafeFileHandle, int> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!PInvoke.CfReferenceProtectedHandle(_protected))
        {
            return Closed;
        }

        try
        {
            return action(_win32);
        }
        finally
        {
            PInvoke.CfReleaseProtectedHandle(_protected);
        }
    }

    public void Dispose()
    {
        _win32.Dispose();
        _protected.Dispose();
    }
}
