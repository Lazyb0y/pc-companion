using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// Window Shading's Windows calls, on the feature's thread. Nothing here waits for another
/// program: calls that do, such as moving a minimized window, go to the thread pool.
/// </summary>
internal sealed class WindowsShadingSystem : IShadingSystem
{
    /// <summary>The window property that marks a rolled-up window.</summary>
    public const string MarkName = "PCCompanion.WindowShading";

    private const uint RollDownItem = 1;
    private const uint CloseItem = 2;

    private static readonly WindowMark Marks = new(MarkName);

    private readonly MessageWindow? _owner;
    private readonly Action<Action> _guard;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private readonly HashSet<nint> _watched = [];

    /// <param name="owner">The feature thread's window, which owns strips' menus and gets answers; none to recover only.</param>
    /// <param name="guard">Runs work on the feature's thread, inside its error boundary.</param>
    public WindowsShadingSystem(MessageWindow? owner, Action<Action> guard, DropDownWindows dropDown, WindowTabsWindows tabs)
    {
        _owner = owner;
        _guard = guard;
        _dropDown = dropDown;
        _tabs = tabs;
    }

    /// <summary>Raised when the first window is watched or the last one isn't, for the thread's hooks.</summary>
    public event EventHandler? WatchingChanged;

    /// <summary>Whether any window is watched.</summary>
    public bool IsWatching => _watched.Count > 0;

    /// <summary>Whether <paramref name="window"/> is watched.</summary>
    public bool IsWatched(nint window) => _watched.Contains(window);

    public nint Foreground => Platform.Windows.Foreground.Window;

    public Point PointerPosition => InputState.PointerPosition ?? new Point(int.MinValue, int.MinValue);

    public ShadeRefusal Check(nint window)
    {
        if (!TopLevelWindows.Exists(window))
        {
            return ShadeRefusal.Gone;
        }

        var processId = TopLevelWindows.ProcessIdOf(window);
        if (processId == _ownProcess)
        {
            return ShadeRefusal.Own;
        }

        if (TopLevelWindows.IsMinimized(window))
        {
            return ShadeRefusal.Minimized;
        }

        var className = TopLevelWindows.ClassName(window);
        if (!TopLevelWindows.MayHaveTaskbarButton(window) || TopLevelWindows.Describe(window).IsDesktop
            || TopLevelWindows.PlaceOf(window) is not { HasFrame: true, IsCloaked: false }
            || className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow")
        {
            return ShadeRefusal.NotMainWindow;
        }

        if (FullScreenCheck.Covers(window))
        {
            return ShadeRefusal.FullScreen;
        }

        // The stand-in Windows shows for a window that stopped responding.
        if (className == "Ghost" || TopLevelWindows.IsNotResponding(window))
        {
            return ShadeRefusal.NotResponding;
        }

        if (!TopLevelWindows.IsEnabled(window))
        {
            return ShadeRefusal.DialogOpen;
        }

        if (_tabs.Contains(window))
        {
            return ShadeRefusal.WindowTab;
        }

        if (_dropDown.Contains(window))
        {
            return ShadeRefusal.DropDown;
        }

        // Windows refuses a program a window of a process with a higher integrity level; one whose
        // level can't be read is left alone too.
        return Elevation.CompareIntegrityLevel(processId) is null or > 0 ? ShadeRefusal.RunsAsAdministrator : ShadeRefusal.None;
    }

    public Rectangle? Frame(nint window) => TopLevelWindows.VisibleBounds(window);

    public Rectangle? Bounds(nint window) => TopLevelWindows.Bounds(window);

    public uint Dpi(nint window) => TopLevelWindows.Dpi(window) is var dpi and > 0 ? dpi : 96;

    public Rectangle? CaptionButtons(nint window) => TopLevelWindows.CaptionButtonBounds(window);

    public bool IsRightToLeft(nint window) => TopLevelWindows.IsLaidOutRightToLeft(window);

    public int SystemCaptionHeight(uint dpi) => TopLevelWindows.CaptionHeight(dpi);

    public void MeasureTitleBar(nint window, Rectangle frame, int band, Action<int?> measured) =>
        TitleBarProbe.Measure(window, frame, band, action => _owner?.BeginInvoke(() => _guard(action)), measured);

    public bool Minimize(nint window, bool inFront) => inFront ? TopLevelWindows.Minimize(window) : TopLevelWindows.ShowMinimized(window);

    public void RollDown(nint window, bool activate)
    {
        if (!activate || !TopLevelWindows.Activate(window))
        {
            TopLevelWindows.RestoreWithoutActivating(window);
        }
    }

    public void RollDownAndClose(nint window)
    {
        RollDown(window, activate: true);
        TopLevelWindows.AskToClose(window);
    }

    // Restoring a window without activating it leaves it where it was in the order of windows,
    // which can be behind the window in front.
    public void ShowWithoutActivating(nint window)
    {
        TopLevelWindows.RestoreWithoutActivating(window);
        TopLevelWindows.BringToTop(window);
    }

    public void PlaceRestored(nint window, Point location)
    {
        if (!TopLevelWindows.IsMaximized(window) && TopLevelWindows.Placement(window) is { RestoresMaximized: false })
        {
            TopLevelWindows.MoveTo(window, location);
        }
    }

    public void MinimizeWithoutActivating(nint window) => TopLevelWindows.ShowMinimized(window);

    // Setting a window's placement waits for its app.
    public void MoveWhileMinimized(nint window, Point offset) => _ = Task.Run(() => TopLevelWindows.MoveNormalPlace(window, offset));

    public bool IsBusy(nint window) => TopLevelWindows.IsBusy(window);

    public bool IsMinimized(nint window) => TopLevelWindows.IsMinimized(window);

    public string Title(nint window) => TopLevelWindows.Title(window);

    public bool Mark(nint window) => Marks.Set(window);

    public void Unmark(nint window) => Marks.Remove(window);

    public IReadOnlyList<nint> FindMarked() => Marks.FindMarked();

    public IShadeStrip CreateStrip(nint window, Rectangle bounds, Rectangle source, bool topmost, string name) =>
        new ShadeStrip(window, bounds, source, topmost, name, Dpi(window), _guard);

    public ShadeMenuChoice ShowMenu(IShadeStrip strip, Point at)
    {
        if (_owner is null)
        {
            return ShadeMenuChoice.None;
        }

        var rightToLeft = Languages.Resolve(CultureInfo.CurrentUICulture).TextInfo.IsRightToLeft;
        var chosen = PopupMenu.Show(_owner, [new PopupMenuItem(RollDownItem, Strings.WindowShadingRollDown), new PopupMenuItem(CloseItem, Strings.WindowShadingClose)], at.X, at.Y, rightToLeft);
        return chosen switch
        {
            RollDownItem => ShadeMenuChoice.RollDown,
            CloseItem => ShadeMenuChoice.Close,
            _ => ShadeMenuChoice.None,
        };
    }

    public void Watch(nint window)
    {
        if (_watched.Add(window) && _watched.Count == 1)
        {
            WatchingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Unwatch(nint window)
    {
        if (_watched.Remove(window) && _watched.Count == 0)
        {
            WatchingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PlayWarning() => WarningSound.Play();
}
