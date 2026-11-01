using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.DropDownApps;

/// <summary>
/// Drop-down Apps' view of Windows. Without a window, as when the feature only brings back windows
/// left marked, it can't watch processes, start apps or place windows off its thread.
/// </summary>
public sealed class WindowsDropDownSystem : IDropDownSystem, IDisposable
{
    /// <summary>The mark's name, which PC Companion keeps from one version to the next.</summary>
    public const string MarkName = "PCCompanion.DropDownApps";

    /// <summary>The name of the mark on windows Drop-down Apps put on top.</summary>
    public const string OnTopMarkName = "PCCompanion.DropDownApps.OnTop";

    private static readonly Lazy<WindowMark> Marks = new(() => new WindowMark(MarkName));
    private static readonly Lazy<WindowMark> OnTopMarks = new(() => new WindowMark(OnTopMarkName));

    private readonly MessageWindow? _window;
    private readonly Action<Action> _guard;
    private readonly IHostCommands? _commands;
    private readonly WindowsAppIdentityReader _identity = new();
    private readonly Dictionary<uint, (WinEventHook Shows, WinEventHook Minimizes)> _watched = [];

    /// <param name="window">The feature thread's window, where reports and results arrive.</param>
    /// <param name="guard">Runs a report inside the feature thread's error boundary.</param>
    /// <param name="commands">Starts apps for the user.</param>
    public WindowsDropDownSystem(MessageWindow? window, Action<Action> guard, IHostCommands? commands)
    {
        ArgumentNullException.ThrowIfNull(guard);

        _window = window;
        _guard = guard;
        _commands = commands;
    }

    public event EventHandler<nint>? WindowShown;

    public event EventHandler<nint>? WindowDestroyed;

    public event EventHandler<nint>? MinimizeStarted;

    public nint ForegroundWindow => Foreground.Window;

    public Point? PointerPosition => InputState.PointerPosition;

    public bool AnimationsOn => WindowsTheme.AreAnimationsOn();

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public TopLevelWindow Describe(nint window) => TopLevelWindows.Describe(window);

    public IReadOnlyList<TopLevelWindow> Windows() => TopLevelWindows.List();

    public string? WindowAppId(nint window) => _identity.WindowAppId(window);

    public string? PackageAppId(uint processId) => _identity.PackageAppId(processId);

    public string? ProgramPath(uint processId) => _identity.ProgramPath(processId);

    public void WaitForFrame() => DesktopComposition.WaitForNextFrame();

    public Point? Location(nint window) => TopLevelWindows.Bounds(window)?.Location;

    public bool MoveTo(nint window, Point location) => TopLevelWindows.MoveTo(window, location);

    public nint RootOwner(nint window) => TopLevelWindows.RootOwner(window);

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public bool IsVisible(nint window) => TopLevelWindows.IsVisible(window);

    public bool IsMinimized(nint window) => TopLevelWindows.IsMinimized(window);

    public bool IsMaximized(nint window) => TopLevelWindows.IsMaximized(window);

    public bool IsCloaked(nint window) => TopLevelWindows.IsCloaked(window);

    public bool IsEnabled(nint window) => TopLevelWindows.IsEnabled(window);

    public bool IsTopmost(nint window) => TopLevelWindows.IsTopmost(window);

    public string ClassName(nint window) => TopLevelWindows.ClassName(window);

    public uint ProcessIdOf(nint window) => TopLevelWindows.ProcessIdOf(window);

    public Rectangle? Bounds(nint window) => TopLevelWindows.Bounds(window);

    public Rectangle? VisibleBounds(nint window) => TopLevelWindows.VisibleBounds(window);

    public WindowPlacement? Placement(nint window) => TopLevelWindows.Placement(window);

    public IReadOnlyList<nint> VisibleOwnedWindows(nint window) => TopLevelWindows.VisibleOwnedWindows(window);

    public DisplayDetails? DisplayAt(Point point) => Displays.At(point);

    public DisplayDetails? MainDisplay() => Displays.Main();

    public DisplayDetails? DisplayOf(nint window) => Displays.Of(window);

    public IReadOnlyList<Rectangle> DisplayAreas() => Displays.GetAreas();

    public bool Place(nint window, Rectangle bounds) => TopLevelWindows.Place(window, bounds);

    public bool ShowWithoutActivating(nint window) => TopLevelWindows.ShowWithoutActivating(window);

    public bool Hide(nint window) => TopLevelWindows.Hide(window);

    public bool ShowMinimized(nint window) => TopLevelWindows.ShowMinimized(window);

    public bool Activate(nint window) => TopLevelWindows.Activate(window);

    public bool GiveFront(nint window) => TopLevelWindows.Activate(window);

    public bool SetTopmost(nint window, bool topmost) => TopLevelWindows.SetTopmost(window, topmost);

    public void SetPlacementLater(nint window, Rectangle normalBounds, bool minimized, bool restoresMaximized, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(done);

        var thread = _window ?? throw new InvalidOperationException("Placing windows needs the feature thread's window.");
        _ = Task.Run(() =>
        {
            var placed = TopLevelWindows.SetPlacement(window, normalBounds, minimized, restoresMaximized);
            thread.BeginInvoke(() => _guard(() => done(placed)));
        });
    }

    public bool Mark(nint window) => Marks.Value.Set(window);

    public void Unmark(nint window) => Marks.Value.Remove(window);

    public bool MarkOnTop(nint window) => OnTopMarks.Value.Set(window);

    public void UnmarkOnTop(nint window) => OnTopMarks.Value.Remove(window);

    public IReadOnlyList<nint> FindMarked() => Marks.Value.FindMarked();

    public IReadOnlyList<nint> FindMarkedOnTop() => OnTopMarks.Value.FindMarked();

    public void PlayWarning() => WarningSound.Play();

    public void StartLater(DropDownApp app, Action<DropDownOutcome> done)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(done);

        var thread = _window ?? throw new InvalidOperationException("Starting apps needs the feature thread's window.");
        var commands = _commands ?? throw new InvalidOperationException("Starting apps needs the host's commands.");

        // The host waits for Windows up to 30 seconds, so apps start on the thread pool.
        _ = Task.Run(() =>
        {
            var outcome = AppStarter.Start(app, commands, File.Exists);
            thread.BeginInvoke(() => _guard(() => done(outcome)));
        });
    }

    public void Watch(uint processId)
    {
        if (_window is null)
        {
            throw new InvalidOperationException("Watching needs the feature thread's window.");
        }

        if (_watched.ContainsKey(processId))
        {
            return;
        }

        // Out-of-context hooks for that process only, which report on this thread.
        var shows = new WinEventHook(WinEventHook.ObjectDestroyEvent, WinEventHook.ObjectShowEvent, processId);
        WinEventHook minimizes;
        try
        {
            minimizes = new WinEventHook(WinEventHook.MinimizeStartEvent, WinEventHook.MinimizeStartEvent, processId);
        }
        catch
        {
            shows.Dispose();
            throw;
        }

        shows.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                (e.EventType == WinEventHook.ObjectShowEvent ? WindowShown : WindowDestroyed)?.Invoke(this, e.Window);
            }
        });
        minimizes.Raised += (_, e) => _guard(() =>
        {
            if (e.IsAboutWindow)
            {
                MinimizeStarted?.Invoke(this, e.Window);
            }
        });
        _watched[processId] = (shows, minimizes);
    }

    public void Unwatch(uint processId)
    {
        if (_watched.Remove(processId, out var hooks))
        {
            hooks.Shows.Dispose();
            hooks.Minimizes.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var processId in _watched.Keys.ToList())
        {
            Unwatch(processId);
        }
    }
}
