using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.ClipboardActions;

/// <summary>What Clipboard Actions' thread uses from Windows; tests use fakes.</summary>
public interface IClipboardActionsSystem
{
    /// <summary>Whether PC Companion runs as administrator, when rules don't run programs.</summary>
    bool IsAdministrator { get; }

    /// <summary>
    /// Registers Windows + Alt + V on the thread's window, or returns <see langword="null"/> when
    /// Windows refused, for example because another program has it.
    /// </summary>
    IShortcut? RegisterShortcut(MessageWindow window);

    /// <summary>Reads the clipboard's text with the thread's window; it may wait for the app that copied it.</summary>
    ClipboardTextResult ReadClipboard(MessageWindow window);

    /// <summary>Where the pointer is, in screen pixels, for the menu.</summary>
    Point PointerPosition();

    /// <summary>
    /// Shows the menu at a screen point, as for a shortcut, and returns the chosen item's ID, or 0.
    /// </summary>
    uint ShowMenu(MessageWindow window, IReadOnlyList<PopupMenuItem> items, Point position);

    /// <summary>Lets the program an action starts come to the front.</summary>
    void AllowFront();

    bool FileExists(string path);

    void StartProcess(ProcessStartInfo startInfo);

    /// <summary>Runs an action off the thread, on the thread pool.</summary>
    void RunInBackground(Action action);
}

/// <summary>Windows + Alt + V, registered with Windows. Use it on the thread that registered it.</summary>
public interface IShortcut : IDisposable
{
    /// <summary>Raised on the registering thread each time the shortcut is pressed.</summary>
    event EventHandler? Pressed;
}
