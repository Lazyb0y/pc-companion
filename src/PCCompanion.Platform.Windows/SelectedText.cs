using System;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>Whether the app of a window may be asked for its selected text, and if not, why.</summary>
public enum SelectedTextApp
{
    /// <summary>The app runs at this process's integrity level, so it may be asked.</summary>
    Readable,

    /// <summary>No window has the keyboard focus or is in front, or it has closed.</summary>
    NoWindow,

    /// <summary>The window is the one Windows shows in place of an app that stopped responding.</summary>
    NotResponding,

    /// <summary>The app runs with more rights, such as an app running as administrator while this process doesn't.</summary>
    RunsAsAdministrator,

    /// <summary>The app runs with fewer rights while this process runs as administrator.</summary>
    RunsWithoutAdminRights,

    /// <summary>The app runs with fewer rights, such as an app in an AppContainer.</summary>
    RunsWithFewerRights,

    /// <summary>The app's rights can't be read.</summary>
    Unknown,
}

/// <summary>What reading the selected text gave; logged by name.</summary>
public enum SelectedTextStatus
{
    /// <summary>The text was read.</summary>
    Read,

    /// <summary>The element with the text has no text selected.</summary>
    NothingSelected,

    /// <summary>No element around the keyboard focus gives its text, or the app answered with an error.</summary>
    NotShared,

    /// <summary>The keyboard focus is in a password box, which isn't read.</summary>
    Password,

    /// <summary>The selected text is longer than the limit the read was given.</summary>
    TooLong,

    /// <summary>
    /// The selection is all of the text of the box that was clicked, as after a click in a browser's
    /// address bar, so it doesn't count as text to paste there.
    /// </summary>
    WholeBoxSelected,

    /// <summary>The app didn't answer within UI Automation's time limits.</summary>
    TimedOut,
}

/// <param name="Status">What reading gave.</param>
/// <param name="Text">
/// The selected text when <paramref name="Status"/> is <see cref="SelectedTextStatus.Read"/>, its
/// parts joined with line breaks; otherwise <see langword="null"/>.
/// </param>
/// <param name="LastLine">
/// The selection's last line on screen, in physical pixels, or <see langword="null"/> when the
/// app gave none on a display.
/// </param>
/// <param name="ErrorCode">The HRESULT of the app's error, for the log, or 0.</param>
public sealed record SelectedTextResult(SelectedTextStatus Status, string? Text = null, Rectangle? LastLine = null, int ErrorCode = 0);

/// <summary>
/// Checks for reading the text selected in the app in front with <see cref="SelectedTextReader"/>.
/// </summary>
public static class SelectedText
{
    /// <summary>The most characters of selected text that are used; one more is read to tell when there are more.</summary>
    public const int MaxLength = 4096;

    // The class of the window Windows shows in place of an app that stopped responding.
    private const string NotRespondingClassName = "Ghost";

    // UI Automation's rectangles are the app's to give, so anything far off any screen isn't one.
    private const double Farthest = 1_000_000;

    /// <summary>
    /// Whether the app of <paramref name="window"/>, the window with the keyboard focus or else the
    /// window in front, may be asked for its selected text: it isn't the window Windows shows in
    /// place of an app that stopped responding, and it runs at this process's integrity level, so
    /// that its answer, objects of the app's, doesn't come from a less trusted program. Windows
    /// blocks UI Automation from reading an app with more rights.
    /// </summary>
    public static unsafe SelectedTextApp CheckWindow(nint window)
    {
        if (window == 0)
        {
            return SelectedTextApp.NoWindow;
        }

        var root = PInvoke.GetAncestor(new HWND(window), GET_ANCESTOR_FLAGS.GA_ROOT);
        if (TopLevelWindows.ClassName(root.IsNull ? window : root) == NotRespondingClassName)
        {
            return SelectedTextApp.NotResponding;
        }

        uint processId = 0;
        if (PInvoke.GetWindowThreadProcessId(new HWND(window), &processId) == 0)
        {
            return SelectedTextApp.NoWindow;
        }

        return Elevation.CompareIntegrityLevel(processId) switch
        {
            0 => SelectedTextApp.Readable,
            > 0 => SelectedTextApp.RunsAsAdministrator,
            < 0 when Elevation.IsElevated => SelectedTextApp.RunsWithoutAdminRights,
            < 0 => SelectedTextApp.RunsWithFewerRights,
            null => SelectedTextApp.Unknown,
        };
    }

    /// <summary>
    /// The last of the rectangles that UI Automation gives for a selection's lines, four numbers
    /// each (left, top, width and height, in physical pixels), when it has a height and is on a
    /// display; otherwise <see langword="null"/>. It's at least one pixel wide, so a menu kept clear
    /// of it can't cover it.
    /// </summary>
    public static Rectangle? LastLine(ReadOnlySpan<double> rectangles)
    {
        if (rectangles.Length < 4)
        {
            return null;
        }

        var last = rectangles[^4..];
        double left = last[0], top = last[1], width = last[2], height = last[3];
        if (!IsPlace(left) || !IsPlace(top) || !(width >= 0 && width < Farthest) || !(height > 0 && height < Farthest))
        {
            return null;
        }

        var line = Rectangle.FromLTRB((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(left + width), (int)Math.Ceiling(top + height));
        if (line.Width < 1)
        {
            line.Width = 1;
        }

        var area = new RECT { left = line.Left, top = line.Top, right = line.Right, bottom = line.Bottom };
        return PInvoke.MonitorFromRect(area, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL).IsNull ? null : line;
    }

    private static bool IsPlace(double value) => value > -Farthest && value < Farthest;
}
