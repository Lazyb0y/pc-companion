using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Which window is under a point, and what part of it a window says is there, such as its title
/// bar, asked without waiting for its program: the question <c>WM_NCHITTEST</c>, which Windows
/// asks the window under the pointer at every move.
/// </summary>
public static class WindowHitTest
{
    // One callback for every request, in a field so the delegate lives as long as the process; each
    // request's answer is found by its number.
    private static readonly SENDASYNCPROC Callback = OnAnswer;
    private static readonly ConcurrentDictionary<nuint, Action<HitTestAnswer>> Pending = new();
    private static long s_lastRequest;

    /// <summary>
    /// The window under <paramref name="point"/>, in physical pixels: the deepest visible child
    /// window there, or 0. It sends other programs' windows no message, so one that stopped
    /// responding can't hold it.
    /// </summary>
    public static nint WindowAt(Point point) => PInvoke.WindowFromPoint(point);

    /// <summary>The top-level window that <paramref name="window"/> is part of, or the window itself.</summary>
    public static nint Root(nint window) => PInvoke.GetAncestor(new HWND(window), GET_ANCESTOR_FLAGS.GA_ROOT);

    /// <summary>The window that <paramref name="window"/> is a child of, or 0 for a top-level window.</summary>
    public static nint Parent(nint window)
    {
        var parent = PInvoke.GetAncestor(new HWND(window), GET_ANCESTOR_FLAGS.GA_PARENT);
        return parent == PInvoke.GetDesktopWindow() ? 0 : parent;
    }

    /// <summary>
    /// Asks <paramref name="window"/> what part of it is at <paramref name="point"/>, in physical
    /// pixels, without waiting for its program. <paramref name="answered"/> gets the answer on
    /// this thread, the next time it handles its messages after the program answered, or at once
    /// for a window of this thread: a program that stopped responding answers once it responds
    /// again, and one that ends first doesn't answer. A window that isn't per-monitor DPI aware is
    /// asked about the point in its own coordinates. Returns false when Windows refused, as for
    /// an app running as administrator while this process doesn't.
    /// </summary>
    /// <remarks><paramref name="answered"/> runs inside a callback from Windows, so guard it.</remarks>
    public static bool Request(nint window, Point point, Action<HitTestAnswer> answered)
    {
        ArgumentNullException.ThrowIfNull(answered);

        var asked = InWindowCoordinates(window, point);
        var request = (nuint)Interlocked.Increment(ref s_lastRequest);
        Pending[request] = answered;
        var lParam = (nint)(int)(((uint)(ushort)asked.Y << 16) | (ushort)asked.X);
        if (PInvoke.SendMessageCallback(new HWND(window), PInvoke.WM_NCHITTEST, 0, lParam, Callback, request))
        {
            return true;
        }

        Pending.TryRemove(request, out _);
        return false;
    }

    // Windows passes a window that isn't per-monitor DPI aware the points it scales for it; another
    // program's question isn't scaled, so it's converted here.
    private static Point InWindowCoordinates(nint window, Point point)
    {
        var awareness = PInvoke.GetAwarenessFromDpiAwarenessContext(PInvoke.GetWindowDpiAwarenessContext(new HWND(window)));
        if (awareness == DPI_AWARENESS.DPI_AWARENESS_PER_MONITOR_AWARE)
        {
            return point;
        }

        var converted = point;
        return PInvoke.PhysicalToLogicalPointForPerMonitorDPI(new HWND(window), ref converted) ? converted : point;
    }

    private static void OnAnswer(HWND window, uint message, nuint request, LRESULT result)
    {
        if (Pending.TryRemove(request, out var answered))
        {
            answered((int)result.Value switch
            {
                (int)PInvoke.HTCAPTION => HitTestAnswer.Caption,
                PInvoke.HTTRANSPARENT => HitTestAnswer.Transparent,
                _ => HitTestAnswer.Elsewhere,
            });
        }
    }
}

/// <summary>What a window says is at a point, as far as finding title bars needs.</summary>
public enum HitTestAnswer
{
    /// <summary>Anything else: its content, such as tabs, a button, a border, or nothing.</summary>
    Elsewhere,

    /// <summary>Its title bar, where dragging moves the window (<c>HTCAPTION</c>).</summary>
    Caption,

    /// <summary>
    /// Nothing of its own: the window under it in the same thread, usually its parent, answers
    /// instead (<c>HTTRANSPARENT</c>).
    /// </summary>
    Transparent,
}
