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

    // A window the taskbar shows that takes input and responds, and isn't the desktop or a taskbar;
    // see "Which windows" in docs/features/windows/title-bar-actions.md.
    public bool IsCandidate(nint root)
    {
        if (!TopLevelWindows.MayHaveTaskbarButton(root) || !TopLevelWindows.IsEnabled(root) || TopLevelWindows.IsNotResponding(root))
        {
            return false;
        }

        var window = TopLevelWindows.Describe(root);
        return !window.IsDesktop && !window.IsCloaked && TopLevelWindows.ClassName(root) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    public AppOfWindow? AppOf(nint root)
    {
        var processId = TopLevelWindows.ProcessIdOf(root);
        return processId == 0 ? null : new AppOfWindow(AppIdentity.OfWindow(root), AppIdentity.OfPackagedProcess(processId), AppIdentity.ProgramPath(processId), processId);
    }

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool Ask(nint window, Point point, Action<HitTestAnswer> answered) => WindowHitTest.Request(window, point, answered);
}
