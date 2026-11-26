using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.WindowShading;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>Windows as Window Shading's controller sees them, kept in memory, with strips it can click.</summary>
internal sealed class FakeShadingSystem : IShadingSystem
{
    private readonly Dictionary<nint, FakeShadedWindow> _windows = [];
    private readonly List<(nint Window, Action<int?> Measured)> _measuring = [];

    public FakeShadedWindow this[nint window] => _windows[window];

    public List<FakeShadeStrip> Strips { get; } = [];

    public List<string> Calls { get; } = [];

    public HashSet<nint> Marks { get; } = [];

    public HashSet<nint> Watched { get; } = [];

    public int Warnings { get; private set; }

    /// <summary>Whether title-bar measurements answer at once; otherwise <see cref="AnswerMeasurements"/> answers them.</summary>
    public bool AnswerAtOnce { get; set; } = true;

    public ShadeMenuChoice MenuChoice { get; set; }

    public nint Foreground { get; set; }

    public Point PointerPosition { get; set; } = new(-1000, -1000);

    public FakeShadedWindow Add(nint window, Rectangle frame)
    {
        var added = new FakeShadedWindow(window) { Frame = frame };
        _windows[window] = added;
        return added;
    }

    public void AnswerMeasurements()
    {
        var waiting = _measuring.ToList();
        _measuring.Clear();
        foreach (var (window, measured) in waiting)
        {
            measured(_windows[window].Measured);
        }
    }

    public ShadeRefusal Check(nint window) => _windows.TryGetValue(window, out var found) ? found.Refusal : ShadeRefusal.Gone;

    public Rectangle? Frame(nint window) => _windows.TryGetValue(window, out var found) && found.Exists ? found.Frame : null;

    // Invisible borders of 7 pixels at the left, right and bottom, as on Windows 11.
    public Rectangle? Bounds(nint window) => Frame(window) is { } frame ? Rectangle.FromLTRB(frame.Left - 7, frame.Top, frame.Right + 7, frame.Bottom + 7) : null;

    public uint Dpi(nint window) => _windows[window].Dpi;

    public Rectangle? CaptionButtons(nint window) => _windows[window].Buttons;

    public bool IsRightToLeft(nint window) => _windows[window].RightToLeft;

    public int SystemCaptionHeight(uint dpi) => (int)Math.Round(31 * dpi / 96.0);

    public void MeasureTitleBar(nint window, Rectangle frame, int band, Action<int?> measured)
    {
        Calls.Add($"measure {window} band {band}");
        if (AnswerAtOnce)
        {
            measured(_windows[window].Measured is { } height ? Math.Min(height, band) : null);
        }
        else
        {
            _measuring.Add((window, measured));
        }
    }

    public bool Minimize(nint window, bool inFront)
    {
        Calls.Add($"minimize {window} in front {inFront}");
        var found = _windows[window];
        if (found.MinimizeRefused)
        {
            return false;
        }

        found.Minimized = true;
        if (Foreground == window)
        {
            Foreground = 0;
        }

        return true;
    }

    public void RollDown(nint window, bool activate)
    {
        Calls.Add($"roll down {window} activate {activate}");
        _windows[window].Minimized = false;
        if (activate)
        {
            Foreground = window;
        }
    }

    public void RollDownAndClose(nint window)
    {
        Calls.Add($"close {window}");
        _windows[window].Minimized = false;
        _windows[window].Exists = false;
    }

    public void ShowWithoutActivating(nint window)
    {
        Calls.Add($"show {window}");
        _windows[window].Minimized = false;
    }

    public void PlaceRestored(nint window, Point location) => Calls.Add($"place {window} at {location.X},{location.Y}");

    public void MinimizeWithoutActivating(nint window)
    {
        Calls.Add($"minimize without activating {window}");
        _windows[window].Minimized = true;
    }

    public void MoveWhileMinimized(nint window, Point offset)
    {
        Calls.Add($"move {window} by {offset.X},{offset.Y}");
        var found = _windows[window];
        found.Frame = new Rectangle(found.Frame.X + offset.X, found.Frame.Y + offset.Y, found.Frame.Width, found.Frame.Height);
    }

    public bool IsBusy(nint window) => _windows[window].Busy;

    public bool IsMinimized(nint window) => _windows.TryGetValue(window, out var found) && found.Minimized;

    public string Title(nint window) => _windows[window].Title;

    public bool Mark(nint window)
    {
        Marks.Add(window);
        return true;
    }

    public void Unmark(nint window) => Marks.Remove(window);

    public IReadOnlyList<nint> FindMarked() => [.. Marks];

    public IShadeStrip CreateStrip(nint window, Rectangle bounds, Rectangle source, bool topmost, string name)
    {
        var strip = new FakeShadeStrip(window, bounds, source, topmost, name);
        Strips.Add(strip);
        return strip;
    }

    public ShadeMenuChoice ShowMenu(IShadeStrip strip, Point at) => MenuChoice;

    public void Watch(nint window) => Watched.Add(window);

    public void Unwatch(nint window) => Watched.Remove(window);

    public void PlayWarning() => Warnings++;
}

internal sealed class FakeShadedWindow(nint handle)
{
    public nint Handle { get; } = handle;

    public Rectangle Frame { get; set; }

    public bool Exists { get; set; } = true;

    public bool Minimized { get; set; }

    public uint Dpi { get; set; } = 96;

    public int? Measured { get; set; } = 31;

    public Rectangle? Buttons { get; set; }

    public bool RightToLeft { get; set; }

    public bool MinimizeRefused { get; set; }

    public bool Busy { get; set; }

    public ShadeRefusal Refusal { get; set; }

    public string Title { get; set; } = "Mail";
}

internal sealed class FakeShadeStrip(nint window, Rectangle bounds, Rectangle source, bool topmost, string name) : IShadeStrip
{
    public event EventHandler<Point>? Clicked;

    public event EventHandler<int>? Wheeled;

    public event EventHandler<Point>? MenuRequested;

    public event EventHandler<Rectangle>? Moved;

    public event EventHandler? PointerEntered;

    public event EventHandler? PointerLeft;

    public nint Window { get; } = window;

    public Rectangle Bounds { get; private set; } = bounds;

    public Rectangle Source { get; } = source;

    public bool Topmost { get; private set; } = topmost;

    public string Name { get; } = name;

    public bool Shown { get; private set; } = true;

    public bool Disposed { get; private set; }

    public void SetTopmost(bool topmost) => Topmost = topmost;

    public void Show() => Shown = true;

    public void Hide() => Shown = false;

    public void Dispose() => Disposed = true;

    public void Click(Point point) => Clicked?.Invoke(this, point);

    public void Wheel(int delta) => Wheeled?.Invoke(this, delta);

    public void RightClick(Point at) => MenuRequested?.Invoke(this, at);

    public void DragTo(Point location)
    {
        Bounds = new Rectangle(location, Bounds.Size);
        Moved?.Invoke(this, Bounds);
    }

    public void Enter() => PointerEntered?.Invoke(this, EventArgs.Empty);

    public void Leave() => PointerLeft?.Invoke(this, EventArgs.Empty);
}
