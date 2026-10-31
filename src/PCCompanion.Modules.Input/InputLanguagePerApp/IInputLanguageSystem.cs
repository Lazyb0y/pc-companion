using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>
/// What <see cref="LanguageSwitcher"/> reads from Windows and changes there, so tests can supply it
/// instead; it reads what tells apps apart as App Windows and Hot Corners do. Use it on the
/// feature's thread. None of it waits for other programs.
/// </summary>
public interface IInputLanguageSystem : IAppIdentityReader
{
    /// <summary>The window in front, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>
    /// Whether Windows' Let me use a different input method for each app window is on; while it's
    /// off, Windows gives an app that comes to the front the language in use.
    /// </summary>
    bool IsPerWindow { get; }

    /// <summary>The window at the end of <paramref name="window"/>'s chain of owners, or the window itself.</summary>
    nint RootOwner(nint window);

    /// <summary>Whether the window is one the taskbar may show: not a tool window, nor owned, unless it asks to be.</summary>
    bool MayHaveTaskbarButton(nint window);

    uint ProcessIdOf(nint window);

    bool Exists(nint window);

    /// <summary>The window that typing goes to, its thread and its process, or <see langword="null"/> when no window is in front.</summary>
    InputFocus? FocusInFront();

    /// <summary>The handle of the thread's input method, or <see langword="null"/> when it has none, as a console program's thread.</summary>
    nint? LayoutOf(uint thread);

    /// <summary>The handles of the user's input methods for the session, in the order of Windows' input switcher.</summary>
    IReadOnlyList<nint> LayoutHandles();

    /// <summary>Posts the request Windows posts when the user switches, asking the window to switch to <paramref name="layout"/>.</summary>
    LayoutRequest RequestSwitch(nint window, nint layout);

    bool IsConsole(nint window);

    /// <summary>
    /// Reads the user's input methods, their identifiers and names off the feature's thread;
    /// <paramref name="read"/> gets them on the feature's thread, or none when they couldn't be read.
    /// </summary>
    void ReadInputMethods(Action<IReadOnlyList<InputMethod>> read);

    /// <summary>
    /// Finds the console host's thread that owns a console window off the feature's thread;
    /// <paramref name="found"/> gets it, or <see langword="null"/> when it isn't found, on the
    /// feature's thread.
    /// </summary>
    void FindConsoleHost(nint window, uint processId, Action<uint?> found);

    /// <summary>Starts or stops calling <see cref="LanguageSwitcher.OnSecond"/> once a second.</summary>
    void ReadEverySecond(bool read);

    /// <summary>Shows the notice with the input method's names.</summary>
    void ShowNotice(InputMethod inputMethod);
}
