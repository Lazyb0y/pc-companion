using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>Whether the strip shows, or why it can't.</summary>
public enum StripState
{
    /// <summary>On the taskbar.</summary>
    Shown,

    /// <summary>Hidden with the taskbar while a full-screen app is in front; it comes back with the taskbar.</summary>
    HiddenWithTaskbar,

    /// <summary>The taskbar isn't running, or didn't answer.</summary>
    NoTaskbar,

    /// <summary>The taskbar hides itself.</summary>
    AutoHide,

    /// <summary>The taskbar has no room for the strip, or isn't along the bottom of the display.</summary>
    NoRoom,
}

/// <summary>
/// Decides where the strip goes and when it shows: it finds room on the taskbar when the taskbar
/// may have changed, draws the meters' values, hides with the taskbar while a full-screen app is in
/// front, and comes back in front of the taskbar when a click brings the taskbar above it. See
/// "On the taskbar" in docs/features/desktop/system-meters.md. Use it on the meters thread.
/// </summary>
public sealed class MeterStrip : IDisposable
{
    /// <summary>The first Windows build the strip runs on: Windows 11 version 22H2.</summary>
    public const int MinimumBuild = 22621;

    /// <summary>The newest Windows build the strip was tested on. Raise it after testing a new release.</summary>
    public const int MaxTestedBuild = 26300;

    // Between the strip and the Widgets entry or the notification area, at 100% scale.
    private const int Gap = 4;

    private readonly IStripHost _host;
    private TaskbarState? _taskbar;
    private IReadOnlyList<TaskbarElement>? _elements;
    private bool _laidOut;
    private (StripStyle Style, uint Dpi, string Culture, string Language)? _measuredFor;
    private StripMetrics? _metrics;
    private Rectangle? _bounds;
    private StripStyle? _style;
    private StripTexts? _texts;
    private StripColors _colors;
    private string _description = string.Empty;
    private (uint[] Pixels, Rectangle Bounds, string Description)? _shown;

    public MeterStrip(IStripHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
    }

    public StripState State { get; private set; } = StripState.NoTaskbar;

    /// <summary>Whether the strip was tested on a Windows build; elsewhere the meters use notification icons.</summary>
    public static bool IsSupportedBuild(int build) => build is >= MinimumBuild and <= MaxTestedBuild;

    /// <summary>
    /// Reads the taskbar again and finds the strip's place, as when the taskbar may have changed,
    /// then shows or hides the strip.
    /// </summary>
    public void LayOut()
    {
        _laidOut = true;
        _taskbar = _host.ReadTaskbar();
        _elements = _taskbar is null ? null : _host.ReadElements(_taskbar.Handle);
        _measuredFor = null;
        Place();
        KeepInSight();
        Draw();
    }

    /// <summary>
    /// Shows new values in a style and colors, and returns where the strip is. The description,
    /// with every meter's name and values, becomes the strip's tooltip and its name for screen readers.
    /// </summary>
    public StripState Show(StripStyle style, StripTexts texts, StripColors colors, string description)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(description);

        (_style, _texts, _colors, _description) = (style, texts, colors, description);
        if (!_laidOut)
        {
            LayOut();
            return State;
        }

        Place();
        KeepInSight();
        Draw();
        return State;
    }

    /// <summary>
    /// Hides the strip while the taskbar isn't in front, as while a full-screen app is, and brings it
    /// back in front of the taskbar when the taskbar came above it.
    /// </summary>
    public void KeepInSight()
    {
        if (_taskbar is null || State is not (StripState.Shown or StripState.HiddenWithTaskbar))
        {
            return;
        }

        var topmost = _host.IsTaskbarTopmost(_taskbar.Handle);
        if (!topmost)
        {
            if (State == StripState.Shown)
            {
                State = StripState.HiddenWithTaskbar;
                Hide();
            }

            return;
        }

        if (State == StripState.HiddenWithTaskbar)
        {
            State = StripState.Shown;
            Draw();
            return;
        }

        _host.KeepInFrontOf(_taskbar.Handle);
    }

    /// <summary>Draws the strip again, as when the pointer comes onto it or leaves it.</summary>
    public void Redraw() => Draw();

    public void Dispose()
    {
        _host.Hide();
        _host.Dispose();
    }

    // Works out the strip's size and place, measuring again only when the style, the scale or the language changed.
    private void Place()
    {
        if (_taskbar is null || _elements is null)
        {
            State = StripState.NoTaskbar;
            return;
        }

        if (_taskbar.IsAutoHide)
        {
            State = StripState.AutoHide;
            return;
        }

        if (_style is null)
        {
            return;
        }

        var key = (_style, _taskbar.Dpi, CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);
        if (_measuredFor != key)
        {
            var scale = _taskbar.Dpi / 96.0;
            _metrics = StripPainter.Measure(_style, scale, CultureInfo.CurrentCulture);
            _bounds = _metrics.Width > 0
                ? StripPlacement.Find(_taskbar.Bounds, _taskbar.NotificationArea, _elements, _metrics.Width, (int)Math.Round(Gap * scale))
                : null;
            _measuredFor = key;
        }

        // Whether it's hidden with the taskbar is up to KeepInSight.
        if (_bounds is null)
        {
            State = StripState.NoRoom;
        }
        else if (State is not (StripState.Shown or StripState.HiddenWithTaskbar))
        {
            State = StripState.Shown;
        }
    }

    // Sends the strip to its window only when its pixels, place or description changed.
    private void Draw()
    {
        if (State != StripState.Shown || _metrics is null || _bounds is not { } bounds || _texts is null)
        {
            Hide();
            return;
        }

        var pixels = StripPainter.Paint(_metrics, bounds.Height, _texts, _colors, _host.IsPointerOver);
        if (_host.IsShown && _shown is { } shown && shown.Bounds == bounds && shown.Description == _description && shown.Pixels.AsSpan().SequenceEqual(pixels))
        {
            return;
        }

        _host.Show(pixels, bounds, _description);
        _shown = (pixels, bounds, _description);
    }

    private void Hide()
    {
        _host.Hide();
        _shown = null;
    }
}
