using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>Finds title bars in Windows itself.</summary>
public sealed class WindowsTitleBarWindows : ITitleBarWindows
{
    public Point? PointerPosition => InputState.PointerPosition;

    public nint WindowAt(Point point) => WindowHitTest.WindowAt(point);

    public nint Root(nint window) => WindowHitTest.Root(window);

    public nint Parent(nint window) => WindowHitTest.Parent(window);

    public Rectangle? VisibleFrame(nint window) => TopLevelWindows.VisibleBounds(window);

    public uint Dpi(nint window) => TopLevelWindows.Dpi(window);

    public bool IsCandidate(nint root) => !TopLevelWindows.IsNotResponding(root);

    public bool Ask(nint window, Point point, Action<HitTestAnswer> answered) => WindowHitTest.Request(window, point, answered);
}
