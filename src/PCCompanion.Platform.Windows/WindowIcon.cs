using System;
using System.Collections.Concurrent;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The icon another program's window shows on the taskbar, read without waiting for the program,
/// and its pixels at a given size.
/// </summary>
public static class WindowIcon
{
    // One callback for every request, in a field so the delegate lives as long as the process; each
    // request's answer is found by its number.
    private static readonly SENDASYNCPROC Callback = OnAnswer;
    private static readonly ConcurrentDictionary<nuint, Action<nint>> Pending = new();
    private static long s_lastRequest;

    /// <summary>
    /// Asks <paramref name="window"/> for its small or large icon without waiting for its program.
    /// <paramref name="answered"/> gets the icon, or 0 when the window has none, on this thread,
    /// the next time it handles its messages after the program answered: a program that stopped
    /// responding answers once it responds again, and one that ends first doesn't answer. Returns
    /// false when Windows refused, for example for an app running as administrator while this
    /// process doesn't.
    /// </summary>
    /// <remarks>
    /// <paramref name="answered"/> runs inside a callback from Windows, so guard it. The icon
    /// belongs to the program, which can destroy it at any time: read its pixels at once.
    /// </remarks>
    public static bool Request(nint window, bool large, Action<nint> answered)
    {
        ArgumentNullException.ThrowIfNull(answered);

        var request = (nuint)Interlocked.Increment(ref s_lastRequest);
        Pending[request] = answered;
        var kind = large ? PInvoke.ICON_BIG : PInvoke.ICON_SMALL2;
        if (PInvoke.SendMessageCallback(new HWND(window), PInvoke.WM_GETICON, kind, 0, Callback, request))
        {
            return true;
        }

        Pending.TryRemove(request, out _);
        return false;
    }

    /// <summary>
    /// The small or large icon of the window's class, or 0 when it has none. Reading it sends the
    /// window no message.
    /// </summary>
    public static nint OfClass(nint window, bool large) =>
        (nint)PInvoke.GetClassLongPtr(new HWND(window), large ? GET_CLASS_LONG_INDEX.GCLP_HICON : GET_CLASS_LONG_INDEX.GCLP_HICONSM);

    /// <summary>
    /// Draws <paramref name="icon"/> at <paramref name="size"/> pixels square and returns its
    /// premultiplied 32-bit pixels, as <see cref="IconImage.FromPixels"/> takes them, or
    /// <see langword="null"/> when Windows couldn't draw it, for example because its program
    /// destroyed it meanwhile.
    /// </summary>
    public static uint[]? Pixels(nint icon, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var pixels = new uint[size * size];
        if (!Draw(icon, size, 0, pixels))
        {
            return null;
        }

        if (Array.Exists(pixels, pixel => pixel >> 24 != 0))
        {
            return pixels;
        }

        // An icon without an alpha channel shows through its mask: drawn on black and on white, the
        // pixels it covers come out the same, and the others take the background.
        var onWhite = new uint[size * size];
        if (!Draw(icon, size, uint.MaxValue, onWhite))
        {
            return null;
        }

        for (var at = 0; at < pixels.Length; at++)
        {
            pixels[at] = pixels[at] == onWhite[at] ? pixels[at] | 0xFF000000 : 0;
        }

        return pixels;
    }

    private static unsafe bool Draw(nint icon, int size, uint background, Span<uint> pixels)
    {
        var dc = PInvoke.CreateCompatibleDC(HDC.Null);
        if (dc.IsNull)
        {
            return false;
        }

        try
        {
            using var bitmap = TopDownBitmap.Create(dc, size, size, out var bits);
            var target = new Span<uint>(bits, size * size);
            target.Fill(background);

            var previous = PInvoke.SelectObject(dc, (HGDIOBJ)bitmap.DangerousGetHandle());
            var drawn = PInvoke.DrawIconEx(dc, 0, 0, new HICON(icon), size, size, 0, HBRUSH.Null, DI_FLAGS.DI_NORMAL);
            PInvoke.GdiFlush();
            PInvoke.SelectObject(dc, previous);

            if (drawn)
            {
                target.CopyTo(pixels);
            }

            return drawn;
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }

    private static void OnAnswer(HWND window, uint message, nuint request, LRESULT result)
    {
        if (Pending.TryRemove(request, out var answered))
        {
            answered(result.Value);
        }
    }
}
