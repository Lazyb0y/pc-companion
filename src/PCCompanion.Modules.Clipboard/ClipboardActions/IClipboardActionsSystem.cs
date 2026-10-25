using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>What Clipboard Actions' thread uses from Windows; tests use fakes.</summary>
public interface IClipboardActionsSystem
{
    /// <summary>
    /// Registers Windows + Alt + V on the thread's window, or returns <see langword="null"/> when
    /// Windows refused, for example because another program has it.
    /// </summary>
    IShortcut? RegisterShortcut(MessageWindow window);

    /// <summary>Reads the clipboard's text with the thread's window; it may wait for the app that copied it.</summary>
    ClipboardTextResult ReadClipboard(MessageWindow window);

    /// <summary>Where the pointer is, in screen pixels, for the menu.</summary>
    Point PointerPosition();

    /// <summary>The app in front and Windows' text cursor of it; doesn't wait for the app.</summary>
    ForegroundTextCursor ReadTextCursor();

    /// <summary>Whether the app of <paramref name="window"/> runs at PC Companion's integrity level, so it may be asked.</summary>
    bool CanAskTextCursor(nint window);

    /// <summary>
    /// Asks the app of <paramref name="window"/> for its accessibility text cursor, off the thread;
    /// the task ends with <see langword="null"/> when the app gives none the user can see.
    /// </summary>
    Task<Rectangle?> AskTextCursor(nint window);

    /// <summary>
    /// Shows the menu at a screen point, as for a shortcut, and returns the chosen item's ID, or 0.
    /// </summary>
    /// <param name="keepClear">The text cursor's line, which the menu doesn't cover, or <see langword="null"/>.</param>
    uint ShowMenu(MessageWindow window, IReadOnlyList<PopupMenuItem> items, Point position, Rectangle? keepClear);

    /// <summary>The window in front, or 0.</summary>
    nint FrontWindow();

    /// <summary>Lets the program an action starts come to the front.</summary>
    void AllowFront();

    /// <summary>
    /// Gives the front back to <paramref name="previous"/>, the window in front before the menu,
    /// while the thread's window still has it, as when an action didn't start; from any thread.
    /// </summary>
    void GiveFrontBack(MessageWindow window, nint previous);

    bool FileExists(string path);

    /// <summary>Runs an action off the thread, on the thread pool.</summary>
    void RunInBackground(Action action);
}

/// <summary>Windows + Alt + V, registered with Windows. Use it on the thread that registered it.</summary>
public interface IShortcut : IDisposable
{
    /// <summary>Raised on the registering thread each time the shortcut is pressed.</summary>
    event EventHandler? Pressed;
}
