using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// Rolls windows up to their title bars and down again: a rolled-up window is minimized, and a
/// strip takes its title bar's place, showing a picture of it. Runs on the feature's thread. See
/// docs/features/windows/window-shading.md.
/// </summary>
public sealed partial class ShadingController : IDisposable
{
    /// <summary>Title Bar Actions' band, the most of a window's top a title bar can take, in effective pixels.</summary>
    public const int Band = 64;

    // The width of a Close button, in effective pixels.
    private const int CloseWidth = 46;

    // A window its app hides this soon after it was rolled up was hidden by being minimized.
    private static readonly TimeSpan HiddenAsMinimized = TimeSpan.FromSeconds(2);

    // How often the pointer is looked at while hover shows a window.
    private static readonly TimeSpan HoverPoll = TimeSpan.FromMilliseconds(100);

    // How long after the wheel rolled a window down it isn't rolled up again, so a wheel that
    // bounces up on its title bar doesn't roll it up at once.
    private static readonly TimeSpan WheelSettle = TimeSpan.FromSeconds(1);

    private static readonly CompositeFormat StripNameFormat = CompositeFormat.Parse(Strings.WindowShadingStripName);

    private readonly IShadingSystem _system;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly ShadedWindows _set;
    private readonly Dictionary<nint, Shaded> _shaded = [];
    private readonly HashSet<nint> _measuring = [];
    private WindowShadingSettings _settings = new();
    private int _order;
    private nint _wheeledDown;
    private long _wheeledDownAt;

    /// <param name="post">Runs work on the feature's thread later, for timers.</param>
    public ShadingController(IShadingSystem system, TimeProvider time, Action<Action> post, ILogger logger, ShadedWindows set)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(set);

        _system = system;
        _time = time;
        _post = post;
        _logger = logger;
        _set = set;
    }

    /// <summary>How many windows are rolled up.</summary>
    public int Count => _shaded.Count;

    /// <summary>The rolled-up windows, for tests and the crash handler.</summary>
    public IReadOnlyCollection<nint> Windows => _shaded.Keys;

    /// <summary>The strip of a rolled-up window, for tests.</summary>
    public IShadeStrip? StripOf(nint window) => _shaded.TryGetValue(window, out var shaded) ? shaded.Strip : null;

    /// <summary>Uses new settings: on top and hover apply to the windows rolled up now too.</summary>
    public void Apply(WindowShadingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var topmostChanged = settings.KeepOnTop != _settings.KeepOnTop;
        _settings = settings;
        foreach (var shaded in _shaded.Values.ToList())
        {
            if (topmostChanged)
            {
                shaded.Strip.SetTopmost(settings.KeepOnTop);
            }

            // Without hover, a window it shows stays rolled down.
            if (!settings.Hover.Enabled && shaded.State != HoverState.None)
            {
                if (shaded.State == HoverState.Shown)
                {
                    Forget(shaded);
                }
                else
                {
                    StopHover(shaded);
                }
            }
        }
    }

    /// <summary>
    /// Rolls a window up, for a click or the wheel on its title bar, or a shortcut: once the window
    /// says where its title bar ends, it's minimized and its strip shows. A window that can't be
    /// rolled up plays the warning sound.
    /// </summary>
    public void RollUp(nint window)
    {
        if (window == 0 || _shaded.ContainsKey(window) || _measuring.Contains(window))
        {
            return;
        }

        if (window == _wheeledDown && _time.GetElapsedTime(_wheeledDownAt) < WheelSettle)
        {
            LogWheelSettling(_logger);
            return;
        }

        var refusal = _system.Check(window);
        if (refusal == ShadeRefusal.None && _system.Frame(window) is { } frame)
        {
            _measuring.Add(window);
            _system.MeasureTitleBar(window, frame, Scale(Band, _system.Dpi(window)), measured => Finish(window, measured));
            return;
        }

        Refuse(refusal == ShadeRefusal.None ? ShadeRefusal.Gone : refusal);
    }

    /// <summary>Rolls up the window in front, for a shortcut.</summary>
    public void RollUpFront() => RollUp(_system.Foreground);

    /// <summary>Rolls down the window rolled up last, in front, for a shortcut. Plays the warning sound when none is.</summary>
    public void RollDownLast()
    {
        if (_shaded.Values.MaxBy(shaded => shaded.Order) is { } last)
        {
            RollDown(last, activate: true, ShadeEnd.Shortcut);
        }
        else
        {
            _system.PlayWarning();
        }
    }

    /// <summary>Rolls every rolled-up window down, without giving any the front.</summary>
    public int RollDownAll() => RollDownAll(ShadeEnd.All);

    /// <summary>A window was restored, by anything: one rolled up is rolled down, and its strip goes.</summary>
    public void OnRestored(nint window)
    {
        if (_shaded.TryGetValue(window, out var shaded) && shaded.State != HoverState.Shown)
        {
            Forget(shaded);
            PlaceMoved(shaded);
            LogRolledDown(_logger, ShadeEnd.Restored);
        }
    }

    /// <summary>A window closed.</summary>
    public void OnDestroyed(nint window)
    {
        _measuring.Remove(window);
        if (_shaded.TryGetValue(window, out var shaded))
        {
            Forget(shaded);
            LogRolledDown(_logger, ShadeEnd.Closed);
        }
    }

    /// <summary>
    /// A window was hidden: one its app hid as it was rolled up, as apps that live in their
    /// notification icon do, is shown again with the warning sound; later, its strip just goes.
    /// </summary>
    public void OnHidden(nint window)
    {
        if (!_shaded.TryGetValue(window, out var shaded))
        {
            return;
        }

        Forget(shaded);
        if (_time.GetElapsedTime(shaded.RolledAt) <= HiddenAsMinimized)
        {
            _system.RollDown(window, activate: false);
            Refuse(ShadeRefusal.HiddenByItsApp);
        }
        else
        {
            LogRolledDown(_logger, ShadeEnd.Hidden);
        }
    }

    /// <summary>A window came to the front: one that hover showed stays rolled down.</summary>
    public void OnForeground(nint window)
    {
        if (_shaded.TryGetValue(window, out var shaded) && shaded.State == HoverState.Shown)
        {
            Forget(shaded);
            LogRolledDown(_logger, ShadeEnd.Hover);
        }
    }

    /// <summary>
    /// Rolls every window down, as the feature stops or the host exits, and when it crashes; it
    /// doesn't throw, so it can run in the crash handler.
    /// </summary>
    public int Release()
    {
        var count = 0;
        foreach (var shaded in _shaded.Values.ToList())
        {
            try
            {
                Forget(shaded);
                _system.RollDown(shaded.Window, activate: false);
                PlaceMoved(shaded);
                count++;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogReleaseFailed(_logger, exception);
            }
        }

        _measuring.Clear();
        return count;
    }

    /// <summary>
    /// Rolls down the windows left rolled up by a PC Companion that ended without rolling them
    /// down, found by their mark: a minimized one is restored without taking the front. Returns how many.
    /// </summary>
    public static int Recover(IShadingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        var count = 0;
        foreach (var window in system.FindMarked())
        {
            if (system.IsMinimized(window))
            {
                system.RollDown(window, activate: false);
                count++;
            }

            system.Unmark(window);
        }

        return count;
    }

    public void Dispose() => Release();

    private int RollDownAll(ShadeEnd why)
    {
        var count = 0;
        foreach (var shaded in _shaded.Values.OrderBy(shaded => shaded.Order).ToList())
        {
            RollDown(shaded, activate: false, why);
            count++;
        }

        return count;
    }

    // The window said where its title bar ends, or didn't in time: it's checked again, since it
    // may have changed meanwhile, then minimized behind its strip.
    private void Finish(nint window, int? measured)
    {
        if (!_measuring.Remove(window))
        {
            return;
        }

        var refusal = _system.Check(window);
        if (refusal != ShadeRefusal.None || _system.Frame(window) is not { } frame || _system.Bounds(window) is not { } bounds)
        {
            Refuse(refusal == ShadeRefusal.None ? ShadeRefusal.Gone : refusal);
            return;
        }

        var dpi = _system.Dpi(window);
        var buttons = _system.CaptionButtons(window);
        var source = measured is > 0 ? TitleBarSource.Measured : buttons is { Height: > 0 } ? TitleBarSource.Buttons : TitleBarSource.System;
        var height = source switch
        {
            TitleBarSource.Measured => measured!.Value,
            TitleBarSource.Buttons => buttons!.Value.Bottom - (frame.Top - bounds.Top),
            _ => _system.SystemCaptionHeight(dpi),
        };
        height = Math.Clamp(height, 2, Math.Min(Scale(Band, dpi), frame.Height));

        // The frame's outermost pixel is left out: the picture has nothing there.
        var strip = Rectangle.FromLTRB(frame.Left + 1, frame.Top + 1, frame.Right - 1, frame.Top + height);
        var picture = new Rectangle(strip.X - bounds.X, strip.Y - bounds.Y, strip.Width, strip.Height);
        var close = CloseButton(window, buttons, bounds, strip, dpi);
        var name = string.Format(CultureInfo.CurrentCulture, StripNameFormat, _system.Title(window));

        // The set has the window before Windows reports the minimize, so Minimize to Tray leaves it,
        // and the picture is taken before the window goes.
        _set.Add(window);
        var created = _system.CreateStrip(window, strip, picture, _settings.KeepOnTop, name);
        if (!_system.Minimize(window, _system.Foreground == window))
        {
            created.Dispose();
            _set.Remove(window);
            Refuse(ShadeRefusal.Refused);
            return;
        }

        _system.Mark(window);
        var shaded = new Shaded(window, created, strip, bounds, close, _time.GetTimestamp(), ++_order);
        _shaded[window] = shaded;
        created.Clicked += (_, point) => OnClicked(shaded, point);
        created.Wheeled += (_, delta) => OnWheeled(shaded, delta);
        created.MenuRequested += (_, at) => OnMenu(shaded, at);
        created.Moved += (_, moved) => OnMoved(shaded, moved);
        created.PointerEntered += (_, _) => OnPointerEntered(shaded);
        created.PointerLeft += (_, _) => OnPointerLeft(shaded);
        _system.Watch(window);
        LogRolledUp(_logger, height, source, dpi);
    }

    // The last of the title bar's buttons, at the right, or at the left right to left, in the strip.
    private Rectangle? CloseButton(nint window, Rectangle? buttons, Rectangle bounds, Rectangle strip, uint dpi)
    {
        if (buttons is not { Width: > 0, Height: > 0 } area)
        {
            return null;
        }

        var width = Math.Min(Scale(CloseWidth, dpi), area.Width);
        var close = _system.IsRightToLeft(window)
            ? new Rectangle(area.Left, area.Top, width, area.Height)
            : new Rectangle(area.Right - width, area.Top, width, area.Height);
        close.Offset(bounds.X - strip.X, bounds.Y - strip.Y);
        return close;
    }

    private void OnClicked(Shaded shaded, Point point)
    {
        if (!IsCurrent(shaded))
        {
            return;
        }

        if (shaded.Close is { } close && close.Contains(point))
        {
            Close(shaded);
        }
        else
        {
            RollDown(shaded, activate: true, ShadeEnd.Click);
        }
    }

    // Only down: up does nothing, so more notches, or a wheel that bounces, never roll it up again.
    private void OnWheeled(Shaded shaded, int delta)
    {
        if (IsCurrent(shaded) && delta < 0)
        {
            RollDown(shaded, activate: true, ShadeEnd.Wheel);
            (_wheeledDown, _wheeledDownAt) = (shaded.Window, _time.GetTimestamp());
        }
    }

    private void OnMenu(Shaded shaded, Point at)
    {
        if (!IsCurrent(shaded))
        {
            return;
        }

        switch (_system.ShowMenu(shaded.Strip, at))
        {
            case ShadeMenuChoice.RollDown when IsCurrent(shaded):
                RollDown(shaded, activate: true, ShadeEnd.Menu);
                break;
            case ShadeMenuChoice.Close when IsCurrent(shaded):
                Close(shaded);
                break;
        }
    }

    // The window's normal place follows the strip, so it rolls down there, whatever rolls it down.
    private void OnMoved(Shaded shaded, Rectangle moved)
    {
        if (!IsCurrent(shaded) || moved.Location == shaded.StripAt.Location)
        {
            return;
        }

        var offset = new Point(moved.X - shaded.StripAt.X, moved.Y - shaded.StripAt.Y);
        shaded.StripAt = moved;
        shaded.Moved = new Point(shaded.Moved.X + offset.X, shaded.Moved.Y + offset.Y);
        _system.MoveWhileMinimized(shaded.Window, offset);
    }

    private void OnPointerEntered(Shaded shaded)
    {
        if (!IsCurrent(shaded) || !_settings.Hover.Enabled || shaded.State != HoverState.None)
        {
            return;
        }

        shaded.State = HoverState.Waiting;
        shaded.Timer = _time.CreateTimer(_ => _post(() => ShowOnHover(shaded)), null, _settings.Hover.RollDownAfter, Timeout.InfiniteTimeSpan);
    }

    private void OnPointerLeft(Shaded shaded)
    {
        if (IsCurrent(shaded) && shaded.State == HoverState.Waiting)
        {
            StopHover(shaded);
        }
    }

    private void ShowOnHover(Shaded shaded)
    {
        if (!IsCurrent(shaded) || shaded.State != HoverState.Waiting)
        {
            return;
        }

        shaded.Timer?.Dispose();
        shaded.State = HoverState.Shown;
        shaded.ShownFrame = null;
        shaded.LeftAt = null;
        shaded.Strip.Hide();
        _system.ShowWithoutActivating(shaded.Window);
        shaded.Timer = _time.CreateTimer(_ => _post(() => WatchHover(shaded)), null, HoverPoll, HoverPoll);
    }

    // While hover shows a window, the pointer is looked at: off the window long enough, it rolls
    // up again, unless the user is working with it.
    private void WatchHover(Shaded shaded)
    {
        if (!IsCurrent(shaded) || shaded.State != HoverState.Shown || _system.Frame(shaded.Window) is not { } frame)
        {
            return;
        }

        // The user moved or sized it: it stays rolled down.
        shaded.ShownFrame ??= frame;
        if (frame != shaded.ShownFrame)
        {
            Forget(shaded);
            LogRolledDown(_logger, ShadeEnd.Hover);
            return;
        }

        if (frame.Contains(_system.PointerPosition) || _system.IsBusy(shaded.Window))
        {
            shaded.LeftAt = null;
            return;
        }

        shaded.LeftAt ??= _time.GetTimestamp();
        if (_time.GetElapsedTime(shaded.LeftAt.Value) < _settings.Hover.RollUpAfter)
        {
            return;
        }

        if (_system.Foreground == shaded.Window)
        {
            Forget(shaded);
            LogRolledDown(_logger, ShadeEnd.Hover);
            return;
        }

        StopHover(shaded);
        _system.MinimizeWithoutActivating(shaded.Window);
        shaded.Strip.Show();
    }

    private static void StopHover(Shaded shaded)
    {
        shaded.Timer?.Dispose();
        shaded.Timer = null;
        shaded.State = HoverState.None;
    }

    private void RollDown(Shaded shaded, bool activate, ShadeEnd why)
    {
        Forget(shaded);
        _system.RollDown(shaded.Window, activate);
        PlaceMoved(shaded);
        LogRolledDown(_logger, why);
    }

    // A window whose strip was dragged comes back where the strip went, whatever place its app keeps.
    private void PlaceMoved(Shaded shaded)
    {
        if (shaded.Moved != Point.Empty)
        {
            _system.PlaceRestored(shaded.Window, new Point(shaded.Bounds.X + shaded.Moved.X, shaded.Bounds.Y + shaded.Moved.Y));
        }
    }

    private void Close(Shaded shaded)
    {
        Forget(shaded);
        _system.RollDownAndClose(shaded.Window);
        LogRolledDown(_logger, ShadeEnd.Close);
    }

    // The window isn't rolled up any more: its strip, mark and watch go.
    private void Forget(Shaded shaded)
    {
        if (!_shaded.Remove(shaded.Window))
        {
            return;
        }

        StopHover(shaded);
        shaded.Strip.Dispose();
        _system.Unwatch(shaded.Window);
        _system.Unmark(shaded.Window);
        _set.Remove(shaded.Window);
    }

    private bool IsCurrent(Shaded shaded) => _shaded.TryGetValue(shaded.Window, out var current) && current == shaded;

    private void Refuse(ShadeRefusal refusal)
    {
        _system.PlayWarning();
        LogRefused(_logger, refusal);
    }

    private static int Scale(int effective, uint dpi) => (int)Math.Round(effective * dpi / 96.0);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading rolled a window up to its title bar, {Height} pixels high at {Dpi} DPI, found by {Source}.")]
    private static partial void LogRolledUp(ILogger logger, int height, TitleBarSource source, uint dpi);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading rolled a window down: {Why}.")]
    private static partial void LogRolledDown(ILogger logger, ShadeEnd why);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading didn't roll a window up: {Refusal}.")]
    private static partial void LogRefused(ILogger logger, ShadeRefusal refusal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Shading didn't roll a window up just after the wheel rolled it down.")]
    private static partial void LogWheelSettling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Shading couldn't roll a window down.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    private enum HoverState
    {
        None,

        /// <summary>The pointer is on the strip, and the delay runs.</summary>
        Waiting,

        /// <summary>Hover shows the window.</summary>
        Shown,
    }

    private sealed class Shaded(nint window, IShadeStrip strip, Rectangle stripAt, Rectangle bounds, Rectangle? close, long rolledAt, int order)
    {
        public nint Window { get; } = window;

        public IShadeStrip Strip { get; } = strip;

        /// <summary>Where the strip is now; the window's normal place follows its moves.</summary>
        public Rectangle StripAt { get; set; } = stripAt;

        /// <summary>The window's rectangle, with its invisible borders, as it was rolled up.</summary>
        public Rectangle Bounds { get; } = bounds;

        /// <summary>How far the strip was dragged in all.</summary>
        public Point Moved { get; set; }

        /// <summary>The picture of the Close button, in the strip, or null when Windows didn't say.</summary>
        public Rectangle? Close { get; } = close;

        public long RolledAt { get; } = rolledAt;

        public int Order { get; } = order;

        public HoverState State { get; set; }

        public ITimer? Timer { get; set; }

        public Rectangle? ShownFrame { get; set; }

        public long? LeftAt { get; set; }
    }
}

/// <summary>How the title bar's height was found, for the log.</summary>
public enum TitleBarSource
{
    /// <summary>The window said where its title bar ends.</summary>
    Measured,

    /// <summary>The height of its caption buttons.</summary>
    Buttons,

    /// <summary>Windows' caption height.</summary>
    System,
}

/// <summary>Why a window was rolled down, for the log.</summary>
public enum ShadeEnd
{
    Click,
    Wheel,
    Menu,
    Close,
    Shortcut,
    All,
    Restored,
    Closed,
    Hidden,
    Hover,
}
