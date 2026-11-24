using System;
using System.Drawing;

namespace PCCompanion.Platform.Windows;

/// <summary>What the pointer did over a window the app draws, as <see cref="LayeredWindow.PointerInput"/> reports it.</summary>
public enum WindowPointerAction
{
    Moved,
    LeftDown,
    LeftUp,
    RightUp,

    /// <summary>Another window got the pointer's input, so a drag the window was following ended.</summary>
    CaptureLost,
}

/// <summary>A pointer action and where it happened, in the window's pixels from its top-left corner.</summary>
public sealed class WindowPointerEventArgs(WindowPointerAction action, Point position) : EventArgs
{
    public WindowPointerAction Action { get; } = action;

    public Point Position { get; } = position;
}
