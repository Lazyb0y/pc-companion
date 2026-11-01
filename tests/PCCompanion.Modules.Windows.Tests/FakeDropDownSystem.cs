using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.DropDownApps;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows that move, show and hide as Windows would make them, at once, on one 1920 x
/// 1080 display, and a record of what the controller did to them. Each frame takes 16 ms.
/// </summary>
internal sealed class FakeDropDownSystem(ManualTimeProvider clock) : IDropDownSystem
{
    public static readonly DisplayDetails Display = new(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1032), 96);

    /// <summary>A second display, right of the first, for the windows on it.</summary>
    public static readonly DisplayDetails SecondDisplay = new(new Rectangle(1920, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1032), 96);

    /// <summary>The invisible borders of every window.</summary>
    public static readonly FrameInsets Borders = new(7, 0, 7, 7);

    /// <summary>The windows, front to back.</summary>
    public List<FakeDropWindow> Windows { get; } = [];

    public List<string> Calls { get; } = [];

    /// <summary>Where each slide's moves went.</summary>
    public List<Point> Moves { get; } = [];

    public HashSet<uint> Watched { get; } = [];

    public int Warnings { get; private set; }

    public nint ForegroundWindow { get; set; }

    public Point? PointerPosition { get; set; } = new Point(100, 100);

    public bool AnimationsOn { get; set; } = true;

    public bool IsMouseButtonHeld { get; set; }

    public List<Rectangle> OtherDisplays { get; } = [];

    /// <summary>Whether placing a minimized or maximized window is done at once; otherwise the test answers the requests.</summary>
    public bool PlacesAtOnce { get; set; } = true;

    public List<(nint Window, Rectangle Normal, bool Minimized, bool RestoresMaximized, Action<bool> Done)> PlacementRequests { get; } = [];

    public FakeDropWindow Add(nint handle, uint processId = 10, string? appId = null, string? program = @"C:\Apps\App.exe")
    {
        var window = new FakeDropWindow { Handle = handle, ProcessId = processId, AppId = appId, Program = program };
        Windows.Add(window);
        return window;
    }

    public FakeDropWindow Get(nint handle) => Windows.Single(window => window.Handle == handle);

    IReadOnlyList<TopLevelWindow> IDropDownSystem.Windows() =>
        [.. Windows.Where(window => window.Visible && !window.Destroyed).Select(Describe)];

    public TopLevelWindow Describe(nint window) => Describe(Get(window));

    public IReadOnlyList<TopLevelWindow> TrayWindows() =>
        [.. Windows.Where(window => window.InTray && !window.Visible && !window.Destroyed).Select(Describe)];

    public string? WindowAppId(nint window) => Find(window)?.AppId;

    public string? PackageAppId(uint processId) => null;

    public string? ProgramPath(uint processId) => Windows.FirstOrDefault(window => window.ProcessId == processId)?.Program;

    public void WaitForFrame() => clock.Advance(TimeSpan.FromMilliseconds(16));

    public Point? Location(nint window) => Find(window)?.Bounds.Location;

    public bool MoveTo(nint window, Point location)
    {
        Get(window).Bounds = Get(window).Bounds with { Location = location };
        Moves.Add(location);
        return true;
    }

    public nint RootOwner(nint window)
    {
        while (Find(window) is { Owner: not 0 } owned)
        {
            window = owned.Owner;
        }

        return window;
    }

    public bool Exists(nint window) => Find(window) is not null;

    public bool IsVisible(nint window) => Find(window)?.Visible ?? false;

    public bool IsMinimized(nint window) => Find(window)?.Minimized ?? false;

    public bool IsMaximized(nint window) => Find(window)?.Maximized ?? false;

    public bool IsCloaked(nint window) => Find(window)?.Cloaked ?? false;

    public bool IsEnabled(nint window) => Find(window)?.Enabled ?? false;

    public bool IsTopmost(nint window) => Find(window)?.Topmost ?? false;

    public string ClassName(nint window) => Find(window)?.ClassName ?? string.Empty;

    public uint ProcessIdOf(nint window) => Find(window)?.ProcessId ?? 0;

    public Rectangle? Bounds(nint window) => Find(window)?.Bounds;

    public Rectangle? VisibleBounds(nint window) => Find(window) is { } found ? Borders.Inside(found.Bounds) : null;

    public WindowPlacement? Placement(nint window) =>
        Find(window) is { } found
            ? new WindowPlacement(found.Normal ?? found.Bounds, found.Minimized ? WindowState.Minimized : found.Maximized ? WindowState.Maximized : WindowState.Normal, found.RestoresMaximized)
            : null;

    public IReadOnlyList<nint> VisibleOwnedWindows(nint window) =>
        [.. Windows.Where(owned => owned.Owner == window && owned.Visible).Select(owned => owned.Handle)];

    public DisplayDetails? DisplayAt(Point point) => Display;

    public DisplayDetails? MainDisplay() => Display;

    public DisplayDetails? DisplayOf(nint window) => Find(window)?.OnSecondDisplay == true ? SecondDisplay : Display;

    public IReadOnlyList<Rectangle> DisplayAreas() => [Display.Bounds, .. OtherDisplays];

    public bool Place(nint window, Rectangle bounds)
    {
        var placed = Get(window);
        placed.Bounds = new Rectangle(bounds.Location, new Size(Math.Max(bounds.Width, placed.MinimumSize.Width), Math.Max(bounds.Height, placed.MinimumSize.Height)));
        Calls.Add($"place {window} {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
        return true;
    }

    public bool ShowWithoutActivating(nint window)
    {
        var shown = Get(window);
        shown.Visible = true;
        shown.Cloaked = shown.StaysCloaked;
        Calls.Add($"show {window}");
        return true;
    }

    public bool Hide(nint window)
    {
        Get(window).Visible = false;
        Calls.Add($"hide {window}");
        return true;
    }

    public bool ShowMinimized(nint window)
    {
        var shown = Get(window);
        (shown.Visible, shown.Minimized) = (true, true);
        Calls.Add($"show minimized {window}");
        return true;
    }

    public bool Activate(nint window)
    {
        ForegroundWindow = window;
        Calls.Add($"activate {window}");
        return true;
    }

    public bool GiveFront(nint window)
    {
        ForegroundWindow = window;
        Calls.Add($"front {window}");
        return true;
    }

    public bool SetTopmost(nint window, bool topmost)
    {
        Get(window).Topmost = topmost;
        Calls.Add($"topmost {window} {topmost}");
        return true;
    }

    public void SetPlacementLater(nint window, Rectangle normalBounds, bool minimized, bool restoresMaximized, Action<bool> done)
    {
        Calls.Add($"placement {window} {normalBounds.X},{normalBounds.Y},{normalBounds.Width},{normalBounds.Height} minimized {minimized}");
        PlacementRequests.Add((window, normalBounds, minimized, restoresMaximized, done));
        if (PlacesAtOnce)
        {
            AnswerPlacement();
        }
    }

    /// <summary>Carries out the oldest placement request, as Windows does once the window's program handles it.</summary>
    public void AnswerPlacement()
    {
        var (window, normal, minimized, restoresMaximized, done) = PlacementRequests[0];
        PlacementRequests.RemoveAt(0);
        var placed = Get(window);
        placed.Normal = normal;
        placed.RestoresMaximized = restoresMaximized;
        (placed.Visible, placed.Minimized, placed.Maximized) = (true, minimized, false);
        if (!minimized)
        {
            placed.Bounds = normal;
        }

        done(true);
    }

    public bool Mark(nint window)
    {
        if (Get(window).RefusesChanges)
        {
            return false;
        }

        Get(window).Marked = true;
        return true;
    }

    public void Unmark(nint window) => Get(window).Marked = false;

    public bool MarkOnTop(nint window)
    {
        Get(window).MarkedOnTop = true;
        return true;
    }

    public void UnmarkOnTop(nint window) => Get(window).MarkedOnTop = false;

    public IReadOnlyList<nint> FindMarked() => [.. Windows.Where(window => window.Marked).Select(window => window.Handle)];

    public IReadOnlyList<nint> FindMarkedOnTop() => [.. Windows.Where(window => window.MarkedOnTop).Select(window => window.Handle)];

    public void PlayWarning() => Warnings++;

    /// <summary>What starting an app answers.</summary>
    public DropDownOutcome StartOutcome { get; set; } = DropDownOutcome.Started;

    /// <summary>The apps started so far.</summary>
    public List<DropDownApp> StartedApps { get; } = [];

    public void StartLater(DropDownApp app, Action<DropDownOutcome> done)
    {
        StartedApps.Add(app);
        done(StartOutcome);
    }

    public void Watch(uint processId) => Watched.Add(processId);

    public void Unwatch(uint processId) => Watched.Remove(processId);

    private static TopLevelWindow Describe(FakeDropWindow window) => new(
        window.Handle,
        window.ProcessId,
        window.Minimized,
        window.Cloaked,
        window.ToolWindow,
        IsAppWindow: false,
        window.NoActivate,
        window.Topmost,
        HasOwner: window.Owner != 0,
        IsDesktop: false);

    private FakeDropWindow? Find(nint window) => Windows.FirstOrDefault(found => found.Handle == window && !found.Destroyed);
}

internal sealed class FakeDropWindow
{
    public nint Handle { get; init; }

    public uint ProcessId { get; init; }

    public string? AppId { get; set; }

    public string? Program { get; set; }

    public string ClassName { get; set; } = "AppWindow";

    public Rectangle Bounds { get; set; } = new(300, 200, 800, 600);

    /// <summary>Its normal rectangle while minimized or maximized, as the last placement set it.</summary>
    public Rectangle? Normal { get; set; }

    public bool RestoresMaximized { get; set; }

    public Size MinimumSize { get; set; }

    public bool Visible { get; set; } = true;

    public bool Minimized { get; set; }

    public bool Maximized { get; set; }

    public bool Cloaked { get; set; }

    /// <summary>Whether it stays cloaked when shown, as on another virtual desktop it doesn't leave.</summary>
    public bool StaysCloaked { get; set; }

    public bool Enabled { get; set; } = true;

    public bool Topmost { get; set; }

    public bool ToolWindow { get; set; }

    public bool NoActivate { get; set; }

    /// <summary>Whether it carries Minimize to Tray's mark, as a window in its tray.</summary>
    public bool InTray { get; set; }

    public bool OnSecondDisplay { get; set; }

    public nint Owner { get; set; }

    public bool Marked { get; set; }

    public bool MarkedOnTop { get; set; }

    /// <summary>Whether Windows refuses to mark it, as for an app running as administrator.</summary>
    public bool RefusesChanges { get; set; }

    public bool Destroyed { get; set; }
}
