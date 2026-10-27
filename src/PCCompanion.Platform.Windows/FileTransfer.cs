using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Moves and copies files without ever replacing a file that's there: when one appears at the
/// last moment, the move or copy fails with <see cref="FileExists"/> or <see cref="AlreadyExists"/>.
/// Both copy the file's streams, such as its Mark of the Web, where the destination's drive keeps them.
/// </summary>
public static class FileTransfer
{
    /// <summary>The error code of a copy that found a file with the destination's name.</summary>
    public const int FileExists = (int)WIN32_ERROR.ERROR_FILE_EXISTS;

    /// <summary>The error code of a move that found a file with the destination's name.</summary>
    public const int AlreadyExists = (int)WIN32_ERROR.ERROR_ALREADY_EXISTS;

    /// <summary>The error code of a move or copy that was cancelled.</summary>
    public const int Cancelled = (int)WIN32_ERROR.ERROR_REQUEST_ABORTED;

    /// <summary>
    /// Moves a file. To another drive, it copies the file and deletes the original only once the
    /// copy is complete; <paramref name="cancel"/> stops such a copy, and the copy is deleted.
    /// Returns 0, or Windows' error code.
    /// </summary>
    public static unsafe int Move(string source, string destination, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        LPPROGRESS_ROUTINE progress = (_, _, _, _, _, _, _, _, _) =>
            cancel.IsCancellationRequested ? COPYPROGRESSROUTINE_PROGRESS.PROGRESS_CANCEL : COPYPROGRESSROUTINE_PROGRESS.PROGRESS_CONTINUE;
        var moved = PInvoke.MoveFileWithProgress(source, destination, progress, null, MOVE_FILE_FLAGS.MOVEFILE_COPY_ALLOWED | MOVE_FILE_FLAGS.MOVEFILE_WRITE_THROUGH);
        var error = moved ? 0 : Marshal.GetLastPInvokeError();
        GC.KeepAlive(progress);
        return error;
    }

    /// <summary>
    /// Copies a file; <paramref name="cancel"/> stops the copy, and the partial copy is deleted.
    /// Returns 0, or Windows' error code.
    /// </summary>
    public static unsafe int Copy(string source, string destination, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);

        // Windows reads the flag while it copies, so a cancel from another thread stops it.
        var flag = (BOOL*)NativeMemory.AllocZeroed((nuint)sizeof(BOOL));
        try
        {
            using var registration = cancel.Register(() => *flag = true);
            var parameters = new COPYFILE2_EXTENDED_PARAMETERS
            {
                dwSize = (uint)Marshal.SizeOf<COPYFILE2_EXTENDED_PARAMETERS>(),
                dwCopyFlags = COPYFILE_FLAGS.COPY_FILE_FAIL_IF_EXISTS,
                pfCancel = flag,
            };

            var result = PInvoke.CopyFile2(source, destination, parameters);
            return result.Succeeded ? 0 : ErrorCode(result);
        }
        finally
        {
            NativeMemory.Free(flag);
        }
    }

    // The Windows error code inside an HRESULT, or the HRESULT itself when it holds none.
    private static int ErrorCode(HRESULT result) =>
        ((uint)result.Value & 0xFFFF0000) == 0x80070000 ? result.Value & 0xFFFF : result.Value;
}
