using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>Which of Windows' own text boxes a window is.</summary>
public enum EditBoxKind
{
    /// <summary>Not one of Windows' own text boxes.</summary>
    None,

    /// <summary>The <c>Edit</c> class, or a class built on it.</summary>
    Plain,

    /// <summary>A rich edit class, or a class built on one.</summary>
    Rich,
}

/// <summary>What putting text into one of Windows' own text boxes gave; logged by name.</summary>
public enum EditBoxResult
{
    /// <summary>The text went in.</summary>
    Inserted,

    /// <summary>
    /// A plain box with more than <see cref="EditBoxes.MaxPlacedLength"/> characters, whose text
    /// cursor can't be placed by message: place it with UI Automation, then call
    /// <see cref="EditBoxes.InsertAtCursor"/>.
    /// </summary>
    NeedsPlacing,

    /// <summary>The point isn't on the box's text, so nothing went in.</summary>
    Outside,

    /// <summary>The box's app didn't answer in time or refused, so nothing went in.</summary>
    NotResponding,
}

/// <summary>
/// Puts text into Windows' own text boxes with window messages, as "Windows' own text boxes" in
/// docs/features/clipboard/middle-click-paste.md says. Each message is sent with
/// <c>SendMessageTimeout</c> and <c>SMTO_ABORTIFHUNG</c>, at most <see cref="MessageTimeout"/>, and
/// a message that fails stops the rest, so an app that stopped responding gets nothing, now or
/// later. Windows copies the text into the app's process, as it does for every message below
/// <c>WM_USER</c>. The calls wait for the app, so make them off the host's UI thread.
/// </summary>
public static class EditBoxes
{
    /// <summary>How long each message waits for the box's app.</summary>
    public static readonly TimeSpan MessageTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest text a plain box may have for its text cursor to be placed by message: <c>EM_CHARFROMPOS</c> gives 16-bit positions.</summary>
    public const int MaxPlacedLength = 65_535;

    private const string WindowsFormsPrefix = "WindowsForms10.";

    /// <summary>
    /// Which box a window class is: <c>Edit</c>, the rich edit classes, such as <c>RICHEDIT50W</c>,
    /// and Windows Forms' classes built on them, such as <c>WindowsForms10.Edit.app.0.1</c>.
    /// </summary>
    public static EditBoxKind KindOf(string className)
    {
        ArgumentNullException.ThrowIfNull(className);

        var name = className;
        if (name.StartsWith(WindowsFormsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[WindowsFormsPrefix.Length..];
            var end = name.IndexOf('.', StringComparison.Ordinal);
            name = end < 0 ? name : name[..end];
        }

        return name.Equals("Edit", StringComparison.OrdinalIgnoreCase) ? EditBoxKind.Plain
            : name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ? EditBoxKind.Rich
            : EditBoxKind.None;
    }

    /// <summary>Which box <paramref name="window"/> is, by its class; <see cref="EditBoxKind.None"/> for no window.</summary>
    public static EditBoxKind KindOf(nint window) => window == 0 ? EditBoxKind.None : KindOf(TopLevelWindows.ClassName(window));

    /// <summary>
    /// The character a plain box's <c>EM_CHARFROMPOS</c> answer names, or <see langword="null"/>
    /// when the point is outside the box's text, which Windows answers with -1.
    /// </summary>
    public static int? CharacterAt(nint answer) => (int)answer == -1 ? null : (int)(answer & 0xFFFF);

    /// <summary>
    /// Puts <paramref name="text"/> into a plain box where <paramref name="point"/>, in physical
    /// pixels, is: the text cursor goes there (<c>EM_CHARFROMPOS</c>, <c>EM_SETSEL</c>), then the
    /// text replaces the empty selection, marked so it can be undone (<c>EM_REPLACESEL</c>).
    /// </summary>
    public static EditBoxResult InsertAt(nint box, Point point, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!Send(box, PInvoke.WM_GETTEXTLENGTH, 0, 0, out var length))
        {
            return EditBoxResult.NotResponding;
        }

        if (length > MaxPlacedLength)
        {
            return EditBoxResult.NeedsPlacing;
        }

        var client = ClientPoint(box, point);
        if (!Send(box, PInvoke.EM_CHARFROMPOS, 0, (nint)(((client.Y & 0xFFFF) << 16) | (client.X & 0xFFFF)), out var answer))
        {
            return EditBoxResult.NotResponding;
        }

        if (CharacterAt(answer) is not { } character)
        {
            return EditBoxResult.Outside;
        }

        return Send(box, PInvoke.EM_SETSEL, (nuint)character, character, out _)
            ? InsertAtCursor(box, text)
            : EditBoxResult.NotResponding;
    }

    /// <summary>
    /// Puts <paramref name="text"/> into a box at its text cursor, replacing what's selected, marked
    /// so it can be undone (<c>EM_REPLACESEL</c>), as for a rich edit box whose cursor UI Automation placed.
    /// </summary>
    public static unsafe EditBoxResult InsertAtCursor(nint box, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        fixed (char* characters = text)
        {
            return Send(box, PInvoke.EM_REPLACESEL, 1, (nint)characters, out _) ? EditBoxResult.Inserted : EditBoxResult.NotResponding;
        }
    }

    // A box of an app that isn't per-monitor DPI aware gets the point in its own pixels, as Windows
    // scales them for it.
    private static Point ClientPoint(nint box, Point point)
    {
        var window = new HWND(box);
        var previous = PInvoke.SetThreadDpiAwarenessContext(PInvoke.GetWindowDpiAwarenessContext(window));
        try
        {
            var converted = point;
            if (PInvoke.GetAwarenessFromDpiAwarenessContext(PInvoke.GetWindowDpiAwarenessContext(window)) != DPI_AWARENESS.DPI_AWARENESS_PER_MONITOR_AWARE)
            {
                PInvoke.PhysicalToLogicalPointForPerMonitorDPI(window, ref converted);
            }

            PInvoke.ScreenToClient(window, ref converted);
            return converted;
        }
        finally
        {
            PInvoke.SetThreadDpiAwarenessContext(previous);
        }
    }

    private static bool Send(nint box, uint message, nuint wParam, nint lParam, out nint result)
    {
        var sent = PInvoke.SendMessageTimeout(new HWND(box), message, wParam, lParam,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, (uint)MessageTimeout.TotalMilliseconds, out var answer);
        result = (nint)answer;
        return sent != 0;
    }
}
