using System.Runtime.InteropServices;
using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Lets another thread cancel a thread's call to another program through COM, such as to Explorer
/// through <see cref="WindowsWallpaper"/>, when the program doesn't answer: the call returns at once
/// with <c>RPC_E_CALL_CANCELED</c>, and the program goes on as before.
/// </summary>
public static class CallCancellation
{
    private const int Cancelled = unchecked((int)0x80010002); // RPC_E_CALL_CANCELED

    /// <summary>The ID of the calling thread, which <see cref="Cancel"/> takes.</summary>
    public static uint CurrentThreadId => PInvoke.GetCurrentThreadId();

    /// <summary>Lets the calling thread's calls be cancelled. Call it on the thread before its calls.</summary>
    /// <exception cref="COMException">Windows refused.</exception>
    public static void Allow() => Marshal.ThrowExceptionForHR(PInvoke.CoEnableCallCancellation().Value);

    /// <summary>
    /// Cancels the call a thread is waiting for, on a thread that allows it.
    /// </summary>
    /// <returns><see langword="false"/> when the thread waits for no call, or doesn't allow it.</returns>
    public static bool Cancel(uint threadId) => PInvoke.CoCancelCall(threadId, 0).Succeeded;

    /// <summary>Whether a failure means that the call was cancelled.</summary>
    public static bool IsCancelled(int hresult) => hresult == Cancelled;
}
