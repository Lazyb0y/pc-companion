using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowRules;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Windows for <see cref="RulesController"/>: two displays side by side, the main one with a
/// taskbar at its bottom, and windows with invisible borders of 7 pixels left, right and below.
/// Placing a window takes effect at once, as Windows does for a window that responds.
/// </summary>
internal sealed class FakeRulesSystem : IRulesSystem
{
    public static readonly Rectangle MainArea = new(0, 0, 1920, 1032);
    public static readonly Rectangle SecondArea = new(1920, 0, 2560, 1392);

    private readonly Dictionary<nint, FakeRuledWindow> _windows = [];

    public event EventHandler<nint>? WindowMoved;

    /// <summary>The chosen displays that are connected, by model, with their work areas.</summary>
    public Dictionary<string, Rectangle> Connected { get; } = new() { ["SECOND"] = SecondArea };

    public Rectangle PointerArea { get; set; } = SecondArea;

    public List<string> Calls { get; } = [];

    public Dictionary<nint, int> Followed { get; } = [];

    public Dictionary<nint, nint> Marks { get; } = [];

    public Rectangle? MainWorkArea => MainArea;

    public Rectangle? PointerWorkArea => PointerArea;

    public FakeRuledWindow this[nint handle] => _windows[handle];

    public FakeRuledWindow Add(nint handle, Rectangle visible, string program = @"C:\Apps\Mail.exe", string title = "Inbox", string className = "MailWindow")
    {
        var window = new FakeRuledWindow(handle) { Visible = visible, Normal = Inflate(visible), Program = program, Title = title, ClassName = className };
        _windows[handle] = window;
        return window;
    }

    public void Remove(nint handle) => _windows.Remove(handle);

    /// <summary>The window's app moves it, as Windows reports to a hook that follows it.</summary>
    public void AppMoves(nint handle, Rectangle visible)
    {
        var window = _windows[handle];
        window.Visible = visible;
        window.Normal = Inflate(visible);
        if (Followed.GetValueOrDefault(handle) > 0)
        {
            WindowMoved?.Invoke(this, handle);
        }
    }

    public RuleRefusal Check(nint window) => _windows.TryGetValue(window, out var found) ? found.Refusal : RuleRefusal.Gone;

    public WindowFacts? Facts(nint window) =>
        _windows.TryGetValue(window, out var found) ? new WindowFacts(new AppOfWindow(found.AppId, null, found.Program, 1), found.Title, found.ClassName) : null;

    public bool Exists(nint window) => _windows.ContainsKey(window);

    public bool IsCloaked(nint window) => _windows[window].Cloaked;

    public bool IsArranged(nint window) => _windows[window].Arranged;

    public bool IsFullScreen(nint window) => _windows[window].FullScreen;

    public Rectangle? Bounds(nint window) => _windows.TryGetValue(window, out var found) ? Inflate(found.Visible) : null;

    public Rectangle? VisibleBounds(nint window) => _windows.TryGetValue(window, out var found) && !found.FrameGone ? found.Visible : null;

    public WindowPlacement? Placement(nint window) =>
        _windows.TryGetValue(window, out var found) ? new WindowPlacement(found.Normal, found.State, RestoresMaximized: false) : null;

    public uint Dpi(nint window) => 96;

    public Rectangle? WorkAreaOf(nint window) => AreaAt(_windows[window].State == WindowState.Normal ? _windows[window].Visible : _windows[window].Normal);

    public Rectangle? WorkAreaOf(RuleDisplay display) => Connected.TryGetValue(display.Model, out var area) ? area : null;

    public string DisplaySet => string.Join(";", Connected.Keys.Order(StringComparer.Ordinal).Prepend("MAIN"));

    public (RuleDisplay Display, Rectangle WorkArea)? DisplayAt(Rectangle frame)
    {
        var area = AreaAt(frame);
        var model = area == SecondArea ? "SECOND" : "MAIN";
        return (new RuleDisplay { Which = "Chosen", Model = model, Name = model }, area);
    }

    public IReadOnlyList<nint> TaskbarWindows() => [.. _windows.Values.Where(window => window.Refusal != RuleRefusal.NotMain).Select(window => window.Handle)];

    public bool Place(nint window, Rectangle bounds)
    {
        Calls.Add($"place {window} {bounds}");
        var found = _windows[window];
        found.Visible = Deflate(bounds);
        found.Normal = bounds;
        return true;
    }

    public void SetPlacement(nint window, Rectangle normalBounds, WindowState show)
    {
        Calls.Add($"placement {window} {normalBounds} {show}");
        var found = _windows[window];
        found.Normal = normalBounds;
        found.State = show;
        found.Visible = show == WindowState.Maximized ? AreaAt(normalBounds) : Deflate(normalBounds);
    }

    public bool IsMinimized(nint window) => _windows[window].State == WindowState.Minimized;

    public bool IsTopmost(nint window) => _windows.TryGetValue(window, out var found) && found.Topmost;

    public bool SetTopmost(nint window, bool topmost)
    {
        Calls.Add($"topmost {window} {topmost}");
        if (_windows.TryGetValue(window, out var found))
        {
            found.Topmost = topmost;
        }

        return true;
    }

    public void RemoveTaskbarButton(nint window)
    {
        Calls.Add($"remove button {window}");
        if (_windows.TryGetValue(window, out var found))
        {
            found.HasButton = false;
        }
    }

    public void AddTaskbarButton(nint window)
    {
        Calls.Add($"add button {window}");
        if (_windows.TryGetValue(window, out var found))
        {
            found.HasButton = true;
        }
    }

    public bool IsLayered(nint window) => _windows[window].Layered;

    public LayeredAttributes? LayeredAttributes(nint window) => _windows[window].Attributes;

    public void SetOpacity(nint window, LayeredAttributes attributes, bool addLayered)
    {
        Calls.Add($"opacity {window} {attributes.Alpha} {addLayered}");
        if (_windows.TryGetValue(window, out var found))
        {
            found.Layered |= addLayered;
            found.Attributes = attributes;
        }
    }

    public void PutBackOpacity(nint window, bool removeLayered, LayeredAttributes? own)
    {
        Calls.Add($"put back opacity {window} {removeLayered}");
        if (_windows.TryGetValue(window, out var found))
        {
            found.Attributes = removeLayered ? null : own;
            found.Layered = !removeLayered;
        }
    }

    /// <summary>Whether Window Tabs runs and takes windows.</summary>
    public bool TabsOn { get; set; } = true;

    public bool GroupWith(nint window, nint target)
    {
        Calls.Add($"group {window} with {target}");
        return TabsOn;
    }

    public bool Mark(nint window, nint value)
    {
        Marks[window] = value;
        return true;
    }

    public nint MarkOf(nint window) => Marks.GetValueOrDefault(window);

    public void Unmark(nint window) => Marks.Remove(window);

    public IReadOnlyList<nint> Marked() => [.. Marks.Keys];

    public void Follow(nint window) => Followed[window] = Followed.GetValueOrDefault(window) + 1;

    public void Unfollow(nint window) => Followed[window] = Followed.GetValueOrDefault(window) - 1;

    public static Rectangle Inflate(Rectangle visible) => Rectangle.FromLTRB(visible.Left - 7, visible.Top, visible.Right + 7, visible.Bottom + 7);

    public static Rectangle Deflate(Rectangle bounds) => Rectangle.FromLTRB(bounds.Left + 7, bounds.Top, bounds.Right - 7, bounds.Bottom - 7);

    private static Rectangle AreaAt(Rectangle frame)
    {
        var center = new Point(frame.Left + (frame.Width / 2), frame.Top + (frame.Height / 2));
        return center.X >= SecondArea.Left ? SecondArea : MainArea;
    }
}

internal sealed class FakeRuledWindow(nint handle)
{
    public nint Handle { get; } = handle;

    public Rectangle Visible { get; set; }

    /// <summary>Whether its visible frame can't be read, as when its app destroys it as it's read.</summary>
    public bool FrameGone { get; set; }

    public Rectangle Normal { get; set; }

    public WindowState State { get; set; }

    public string? AppId { get; set; }

    public string Program { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public bool Cloaked { get; set; }

    public bool Arranged { get; set; }

    public bool FullScreen { get; set; }

    public RuleRefusal Refusal { get; set; }

    public bool Topmost { get; set; }

    public bool HasButton { get; set; } = true;

    public bool Layered { get; set; }

    public LayeredAttributes? Attributes { get; set; }
}
