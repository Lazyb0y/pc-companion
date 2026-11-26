using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// Reads the pointer, buttons, keys, full-screen state, windows, the app in front and the bars that
/// hide themselves from Windows.
/// </summary>
internal sealed class WindowsHotCornerEnvironment : IHotCornerEnvironment
{
    // The last answer for each side of each display, and the questions Explorer hasn't answered yet.
    private readonly ConcurrentDictionary<(Rectangle Display, ScreenSide Side), bool> _autoHideBars = new();
    private readonly ConcurrentDictionary<(Rectangle Display, ScreenSide Side), bool> _asking = new();

    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public Rectangle? PointerClip => InputState.PointerClip;

    public PointerVisibility PointerVisibility => InputState.PointerVisibility;

    public FullScreenMode FullScreenMode => Foreground.FullScreenMode;

    public Rectangle? FrontWindowDisplay => Displays.AreaOf(Foreground.Window);

    public AppOfWindow? AppInFront
    {
        get
        {
            var window = Foreground.Window;
            var processId = window == 0 ? 0 : TopLevelWindows.ProcessIdOf(window);
            return processId == 0 ? null : new AppOfWindow(AppIdentity.OfWindow(window), AppIdentity.OfPackagedProcess(processId), AppIdentity.ProgramPath(processId), processId);
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

    public bool HasAutoHideBar(Rectangle display, ScreenSide side) => _autoHideBars.TryGetValue((display, side), out var hides) && hides;

    /// <summary>
    /// Asks Explorer again, on the thread pool, whether a bar hides itself on the side, as the
    /// pointer reaches an edge there; <see cref="HasAutoHideBar"/> gives the answer once it comes.
    /// One question at a time for each side, so an Explorer that stopped responding doesn't pile them up.
    /// </summary>
    public void AskAutoHideBar(Rectangle display, ScreenSide side)
    {
        if (!_asking.TryAdd((display, side), true))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _autoHideBars[(display, side)] = Taskbar.HasAutoHideBar(display, side);
            }
            finally
            {
                _asking.TryRemove((display, side), out _);
            }
        });
    }
}
