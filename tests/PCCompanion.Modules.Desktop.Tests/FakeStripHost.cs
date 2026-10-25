using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Desktop.Tests;

/// <summary>
/// A taskbar and a strip window that tests set and look at: a 1920 by 48 taskbar at 100% scale,
/// aligned left, with Start, Search, Task View, five app buttons ending at 1188, the Widgets entry
/// at 1510 and the notification area at 1674.
/// </summary>
internal sealed class FakeStripHost(Action<string>? record = null) : IStripHost
{
    public static readonly Rectangle TaskbarBounds = new(0, 1032, 1920, 48);
    public static readonly Rectangle NotificationArea = new(1674, 1032, 246, 48);
    public static readonly Rectangle Widgets = new(1510, 1032, 152, 48);

    public event EventHandler? LayoutMayHaveChanged;

    public event EventHandler? ZOrderMayHaveChanged;

    public event EventHandler? PointerOverChanged;

    public event EventHandler? Clicked;

    public event EventHandler<TrayMenuEventArgs>? MenuRequested;

    public TaskbarState? Taskbar { get; set; } = new(1, 100, TaskbarBounds, NotificationArea, TaskBand: null, 96, IsTopmost: true, IsAutoHide: false);

    public List<TaskbarElement>? Elements { get; set; } = DefaultElements();

    public bool TaskbarTopmost { get; set; } = true;

    /// <summary>Whether the taskbar came in front of the strip.</summary>
    public bool TaskbarInFront { get; set; }

    public bool IsShown { get; private set; }

    public bool IsPointerOver { get; set; }

    public int TaskbarReads { get; private set; }

    public int Shows { get; private set; }

    public int Raises { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Whether the host watches the taskbar for changes, as it does until the meters pause.</summary>
    public bool IsWatching { get; private set; } = true;

    public (uint[] Pixels, Rectangle Bounds, string Description)? Last { get; private set; }

    public static List<TaskbarElement> DefaultElements()
    {
        var elements = new List<TaskbarElement>
        {
            new(new Rectangle(0, 1032, 55, 48), "StartButton", "ToggleButton"),
            new(new Rectangle(57, 1040, 220, 32), "SearchButton", "ToggleButton"),
            new(new Rectangle(279, 1032, 44, 48), "TaskViewButton", "ToggleButton"),
        };

        for (var x = 323; x < 1188; x += 173)
        {
            elements.Add(new(new Rectangle(x, 1032, 173, 48), "Window: 0x1", StripPlacement.AppButtonClass));
        }

        elements.Add(new(Widgets, "WidgetsButton", "ToggleButton"));
        elements.Add(new(NotificationArea, string.Empty, "TrayNotifyWnd"));
        return elements;
    }

    public TaskbarState? ReadTaskbar()
    {
        TaskbarReads++;
        return Taskbar;
    }

    public IReadOnlyList<TaskbarElement>? ReadElements(nint taskbar) => Elements;

    public bool IsTaskbarTopmost(nint taskbar) => TaskbarTopmost;

    public void Show(uint[] pixels, Rectangle bounds, string description)
    {
        Assert.Equal(bounds.Width * bounds.Height, pixels.Length);
        Shows++;
        IsShown = true;
        Last = (pixels, bounds, description);
        record?.Invoke("strip show");
    }

    public void Hide()
    {
        if (IsShown)
        {
            record?.Invoke("strip hide");
        }

        IsShown = false;
    }

    public bool KeepInFrontOf(nint taskbar)
    {
        if (!IsShown || !TaskbarInFront)
        {
            return false;
        }

        TaskbarInFront = false;
        Raises++;
        return true;
    }

    public void PauseWatching()
    {
        IsWatching = false;
        record?.Invoke("strip pause");
    }

    public void ResumeWatching()
    {
        IsWatching = true;
        record?.Invoke("strip resume");
    }

    public void RaiseLayoutMayHaveChanged() => LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseZOrderMayHaveChanged() => ZOrderMayHaveChanged?.Invoke(this, EventArgs.Empty);

    public void RaisePointerOverChanged() => PointerOverChanged?.Invoke(this, EventArgs.Empty);

    public void Click() => Clicked?.Invoke(this, EventArgs.Empty);

    public void RightClick() => MenuRequested?.Invoke(this, new TrayMenuEventArgs(1500, 1040));

    public void Dispose()
    {
        IsDisposed = true;
        record?.Invoke("strip dispose");
    }
}
