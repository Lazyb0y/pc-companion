using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Modules.Input.HotCorners;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Input.Tests;

/// <summary>
/// The pointer, buttons, keys, full-screen state, windows and the app in front, as a test sets them.
/// </summary>
internal sealed class FakeHotCornerEnvironment : IHotCornerEnvironment
{
    public Point? PointerPosition { get; set; }

    public bool IsMouseButtonHeld { get; set; }

    public Rectangle? PointerClip { get; set; }

    public PointerVisibility PointerVisibility { get; set; }

    public FullScreenMode FullScreenMode { get; set; }

    public Rectangle? FrontWindowDisplay { get; set; }

    public AppOfWindow? AppInFront
    {
        get
        {
            AppReads++;
            return FrontApp;
        }
    }

    /// <summary>The app <see cref="AppInFront"/> returns.</summary>
    public AppOfWindow? FrontApp { get; set; }

    /// <summary>How many times the app in front was read.</summary>
    public int AppReads { get; private set; }

    public HotCornerKey HeldKey { get; set; }

    /// <summary>The visible top-level windows, front to back.</summary>
    public List<WindowPlace> Windows { get; } = [];

    /// <summary>How many times the windows were read.</summary>
    public int WindowSearches { get; private set; }

    /// <summary>How many windows the last search read.</summary>
    public int WindowsRead { get; private set; }

    public bool IsKeyHeld(HotCornerKey key) => key == HeldKey;

    public bool? FindWindowFrontToBack(Func<WindowPlace, bool?> decide)
    {
        WindowSearches++;
        WindowsRead = 0;
        foreach (var window in Windows)
        {
            WindowsRead++;
            if (decide(window) is { } decision)
            {
                return decision;
            }
        }

        return null;
    }
}
