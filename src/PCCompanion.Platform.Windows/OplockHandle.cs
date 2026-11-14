using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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

    /// <summary>
    /// Reads from <paramref name="offset"/> while Windows keeps the handle open, and returns how
    /// many bytes it read, or -1 when a program opened the file and Windows closed it.
    /// </summary>
    /// <exception cref="IOException">The file can't be read.</exception>
    public unsafe int Read(Span<byte> buffer, long offset)
    {
        if (!PInvoke.CfReferenceProtectedHandle(_protected))
        {
            return -1;
        }

        try
        {
            // The handle is asynchronous, and Windows bound it to a completion port of its own, so
            // .NET can't read it: each read waits for an event, whose low bit keeps the
            // completion off that port.
            using var done = new ManualResetEvent(initialState: false);
            var overlapped = new NativeOverlapped
            {
                OffsetLow = unchecked((int)offset),
                OffsetHigh = (int)(offset >> 32),
                EventHandle = done.SafeWaitHandle.DangerousGetHandle() | 1,
            };
            var handle = (HANDLE)_win32.DangerousGetHandle();
            uint read = 0;
            fixed (byte* bytes = buffer)
            {
                if (!PInvoke.ReadFile(handle, bytes, (uint)buffer.Length, null, &overlapped))
                {
                    var error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
                    if (error == WIN32_ERROR.ERROR_HANDLE_EOF)
                    {
                        return 0;
                    }

                    if (error != WIN32_ERROR.ERROR_IO_PENDING)
                    {
                        throw new IOException($"The file can't be read (error {(int)error}).", HResultOf(error));
                    }
                }

                if (!PInvoke.GetOverlappedResult(handle, &overlapped, &read, bWait: true))
                {
                    var error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
                    return error == WIN32_ERROR.ERROR_HANDLE_EOF
                        ? 0
                        : throw new IOException($"The file can't be read (error {(int)error}).", HResultOf(error));
                }
            }

            return (int)read;
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

    private static int HResultOf(WIN32_ERROR error) => unchecked((int)0x80070000 | ((int)error & 0xFFFF));
}
