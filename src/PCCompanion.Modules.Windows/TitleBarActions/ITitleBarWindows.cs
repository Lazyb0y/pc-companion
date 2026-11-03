using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// What finding title bars reads from Windows and asks windows, so tests can supply it instead.
/// Use it on one thread. None of it waits for the windows' programs.
/// </summary>
public interface ITitleBarWindows
{
    /// <summary>The pointer's position in physical pixels, or <see langword="null"/> when it can't be read.</summary>
    Point? PointerPosition { get; }

    /// <summary>The deepest visible window under <paramref name="point"/>, or 0.</summary>
    nint WindowAt(Point point);

    /// <summary>The top-level window that <paramref name="window"/> is part of.</summary>
    nint Root(nint window);

    /// <summary>The window that <paramref name="window"/> is a child of, or 0 for a top-level window.</summary>
    nint Parent(nint window);

    /// <summary>The part of the window that shows, in physical pixels, or <see langword="null"/> when it has closed.</summary>
    Rectangle? VisibleFrame(nint window);

    /// <summary>The DPI the window is drawn at: 96 at 100% scale.</summary>
    uint Dpi(nint window);

    /// <summary>Whether the top-level window's title bar may get actions; one whose program stopped responding never does.</summary>
    bool IsCandidate(nint root);

    /// <summary>The top-level window's app, as the taskbar groups apps, or <see langword="null"/> when it has closed.</summary>
    AppOfWindow? AppOf(nint root);

    bool Exists(nint window);

    /// <summary>
    /// Asks <paramref name="window"/> what part of it is at <paramref name="point"/>; the answer
    /// comes later on this thread, or at once for a window of this thread. Returns false when
    /// Windows refused.
    /// </summary>
    bool Ask(nint window, Point point, Action<HitTestAnswer> answered);
}
