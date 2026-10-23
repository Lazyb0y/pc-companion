using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Reads the pointer, buttons, keys, full-screen state, windows and the app in front from Windows.
/// </summary>
internal sealed class WindowsHotCornerEnvironment : IHotCornerEnvironment
{
    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public Rectangle? PointerClip => InputState.PointerClip;

    public PointerVisibility PointerVisibility => InputState.PointerVisibility;

    public FullScreenMode FullScreenMode => Foreground.FullScreenMode;

    public Rectangle? FrontWindowDisplay => Displays.AreaOf(Foreground.Window);

    public AppInFront? AppInFront
    {
        get
        {
            var window = Foreground.Window;
            var processId = window == 0 ? 0 : TopLevelWindows.ProcessIdOf(window);
            return processId == 0 ? null : new AppInFront(AppIdentity.OfWindow(window), AppIdentity.OfPackagedProcess(processId), AppIdentity.ProgramPath(processId));
        }
    }

    public bool IsKeyHeld(HotCornerKey key) => InputState.IsModifierHeld(key switch
    {
        HotCornerKey.Control => ModifierKey.Control,
        HotCornerKey.Alt => ModifierKey.Alt,
        HotCornerKey.Shift => ModifierKey.Shift,
        HotCornerKey.Windows => ModifierKey.Windows,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    });

    public bool? FindWindowFrontToBack(Func<WindowPlace, bool?> decide) => TopLevelWindows.FindFrontToBack(decide);
}
