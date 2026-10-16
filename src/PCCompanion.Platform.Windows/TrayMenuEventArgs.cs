using System;

namespace PCCompanion.Platform.Windows;

/// <summary>The screen point, in physical pixels, to show the tray icon's menu at.</summary>
public sealed class TrayMenuEventArgs(int x, int y) : EventArgs
{
    public int X { get; } = x;

    public int Y { get; } = y;
}
