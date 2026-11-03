using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The layered style of another program's window, which lets Windows show it with an opacity of
/// its own (<c>WS_EX_LAYERED</c> and <c>SetLayeredWindowAttributes</c>). Windows refuses it for a
/// window of an app running as administrator while this process doesn't.
/// </summary>
public static class LayeredStyle
{
    /// <summary>Whether <paramref name="window"/> has the layered style. It sends the window no message.</summary>
    public static bool IsLayered(nint window) => (Style(window) & WINDOW_EX_STYLE.WS_EX_LAYERED) != 0;

    /// <summary>
    /// The opacity and color key a layered window shows with, or <see langword="null"/> when it
    /// draws itself with <c>UpdateLayeredWindow</c>, or has no layered style. It sends the window no message.
    /// </summary>
    public static unsafe LayeredAttributes? Read(nint window)
    {
        COLORREF key;
        byte alpha;
        LAYERED_WINDOW_ATTRIBUTES_FLAGS flags;
        return PInvoke.GetLayeredWindowAttributes(new HWND(window), &key, &alpha, &flags)
            ? new LayeredAttributes(key.Value, alpha, (LayeredEffects)flags)
            : null;
    }

    /// <summary>
    /// Shows a layered window with <paramref name="attributes"/>. Returns false when Windows refused.
    /// It doesn't wait for the window's program.
    /// </summary>
    /// <remarks>
    /// A window that draws itself with <c>UpdateLayeredWindow</c> can't any more once this is set,
    /// so check with <see cref="Read"/> first.
    /// </remarks>
    public static bool Set(nint window, LayeredAttributes attributes) =>
        PInvoke.SetLayeredWindowAttributes(new HWND(window), new COLORREF(attributes.ColorKey), attributes.Alpha, (LAYERED_WINDOW_ATTRIBUTES_FLAGS)attributes.Effects);

    /// <summary>
    /// Gives <paramref name="window"/> the layered style, or takes it away. Returns false when
    /// Windows refused. Windows sends the window <c>WM_STYLECHANGING</c> and waits for its program,
    /// so don't call it on a thread that must not wait.
    /// </summary>
    public static bool SetLayered(nint window, bool layered)
    {
        var style = Style(window);
        var changed = layered ? style | WINDOW_EX_STYLE.WS_EX_LAYERED : style & ~WINDOW_EX_STYLE.WS_EX_LAYERED;
        if (changed == style)
        {
            return true;
        }

        Marshal.SetLastPInvokeError(0);
        return PInvoke.SetWindowLongPtr(new HWND(window), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)(uint)changed) != 0 || Marshal.GetLastPInvokeError() == 0;
    }

    private static WINDOW_EX_STYLE Style(nint window) =>
        (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLongPtr(new HWND(window), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
}

/// <summary>How a layered window shows: its opacity, and a color shown as transparent.</summary>
/// <param name="ColorKey">The color shown as transparent, with <see cref="LayeredEffects.ColorKey"/>, as 0x00BBGGRR.</param>
/// <param name="Alpha">The opacity, with <see cref="LayeredEffects.Alpha"/>: 0 for invisible to 255 for opaque.</param>
/// <param name="Effects">Which of the two apply.</param>
public readonly record struct LayeredAttributes(uint ColorKey, byte Alpha, LayeredEffects Effects);

/// <summary>Which of a layered window's attributes apply.</summary>
[System.Flags]
public enum LayeredEffects : uint
{
    None = 0,

    /// <summary>The color key is shown as transparent (<c>LWA_COLORKEY</c>).</summary>
    ColorKey = 1,

    /// <summary>The window shows with its alpha (<c>LWA_ALPHA</c>).</summary>
    Alpha = 2,
}
