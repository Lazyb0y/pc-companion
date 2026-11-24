using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// A group's tab strip: a window PC Companion draws, right above the group's window in the order
/// of windows, that never takes the focus. It reads its tabs' titles and icons, follows clicks,
/// drags and right-clicks, and gives screen readers a tab list. Create, use and dispose it on the
/// feature's thread.
/// </summary>
internal sealed class TabStrip : ITabStrip, ITabList
{
    private const uint MoveOutItem = 1;
    private const uint CloseItem = 2;
    private const uint SettingsItem = 3;

    // Effective pixels the pointer moves with the button held before a press is a drag.
    private const int DragThreshold = 4;

    private static int s_count;

    private readonly LayeredWindow _window;
    private readonly TabListAutomation _automation;
    private readonly MessageWindow _menuOwner;
    private readonly Action<Action> _guard;
    private readonly bool _rightToLeft;
    private readonly Dictionary<nint, TabLook> _looks = [];
    private StripView? _view;
    private TabColors? _colors;
    private double? _textScale;
    private nint _above;
    private int _dpi = 96;
    private int _hover = -1;
    private int _pressed = -1;
    private Point _pressedAt;
    private bool _dragging;
    private bool _dropTarget;

    /// <param name="menuOwner">The feature thread's window, which owns the tab's menu.</param>
    /// <param name="guard">Runs work that Windows calls back for, such as an icon arriving, inside the thread's error boundary.</param>
    public TabStrip(MessageWindow menuOwner, Action<Action> guard, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(menuOwner);
        ArgumentNullException.ThrowIfNull(guard);

        _menuOwner = menuOwner;
        _guard = guard;
        _rightToLeft = rightToLeft;
        _window = new LayeredWindow($"PCCompanion.WindowTabs.Strip.{Interlocked.Increment(ref s_count)}", Strings.WindowTabsName, topmost: false);
        _automation = new TabListAutomation(_window, this);
        _window.PointerInput += (_, e) => _guard(() => OnPointer(e));
        _window.PointerOverChanged += (_, _) => _guard(() =>
        {
            if (!_window.IsPointerOver && _hover >= 0)
            {
                _hover = -1;
                Draw();
            }
        });
        _window.MessageReceived += (_, e) => _guard(() =>
        {
            // The colors, the text size or the display's scale changed.
            if (WindowsTheme.IsChangeMessage(e.Message))
            {
                _colors = null;
                _textScale = null;
                Draw();
            }
        });
    }

    public event EventHandler<int>? TabChosen;

    public event EventHandler<TabDrop>? TabDropped;

    public event EventHandler<TabMenuChoice>? MenuChosen;

    public bool IsPointerOver => _window.IsPointerOver || _dragging;

    public string Name => _view is null || Selected < 0 ? Strings.WindowTabsName : string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.WindowTabsListName), NameOf(Selected));

    public int Count => _view?.Tabs.Count ?? 0;

    public int Selected => _view is null ? -1 : _view.Chosen >= 0 ? _view.Chosen : _view.Front;

    public string NameOf(int index) => _view is { } view && index >= 0 && index < view.Tabs.Count && _looks.TryGetValue(view.Tabs[index], out var look) ? look.Title : string.Empty;

    public Rectangle BoundsOf(int index)
    {
        if (_view is not { } view || index < 0 || index >= view.Place.Tabs.Count)
        {
            return Rectangle.Empty;
        }

        var tab = view.Place.Tabs[index];
        return tab with { X = tab.X + view.Place.Bounds.X, Y = tab.Y + view.Place.Bounds.Y };
    }

    public void Choose(int index) => TabChosen?.Invoke(this, index);

    public void Show(StripView view, nint window)
    {
        ArgumentNullException.ThrowIfNull(view);

        var tabsChanged = _view is null || !_view.Tabs.SequenceEqual(view.Tabs);
        var selectionChanged = _view is null || Selected != (view.Chosen >= 0 ? view.Chosen : view.Front);
        _view = view;
        _above = window;
        _dpi = (int)(Displays.Of(window)?.Dpi ?? 96);
        foreach (var tab in view.Tabs.Where(tab => !_looks.ContainsKey(tab)))
        {
            Read(tab);
        }

        foreach (var gone in _looks.Keys.Where(tab => !view.Tabs.Contains(tab)).ToList())
        {
            _looks.Remove(gone);
        }

        Draw();
        if (!_dropTarget)
        {
            _window.PlaceAbove(window);
        }

        if (tabsChanged)
        {
            _automation.TabsChanged();
        }

        if (selectionChanged)
        {
            _automation.SelectionChanged();
        }
    }

    public void Hide()
    {
        EndPress();
        _window.Hide();
    }

    public void SetDropTarget(bool isTarget)
    {
        _dropTarget = isTarget;
        if (!_window.IsShown)
        {
            return;
        }

        _window.SetTopmost(isTarget);
        if (!isTarget && _above != 0)
        {
            _window.PlaceAbove(_above);
        }
    }

    public void TitleChanged(nint window)
    {
        if (!_looks.TryGetValue(window, out var look))
        {
            return;
        }

        _looks[window] = look with { Title = TopLevelWindows.Title(window) };
        Draw();
        _automation.TabsChanged();
    }

    public void Announce(string text) => _automation.Announce(text);

    public void Dispose()
    {
        _automation.Dispose();
        _window.Dispose();
    }

    // The title is Windows' copy, read without a message; the icon is the window class's at once,
    // then the window's own once its app answers, without waiting for it.
    private void Read(nint window)
    {
        var size = StripLayout.Scale(TabStripPainter.IconSize, _dpi);
        var classIcon = WindowIcon.OfClass(window, large: false);
        _looks[window] = new TabLook(TopLevelWindows.Title(window), classIcon == 0 ? null : WindowIcon.Pixels(classIcon, size));
        WindowIcon.Request(window, large: false, icon => _guard(() =>
        {
            if (icon != 0 && _looks.TryGetValue(window, out var look) && WindowIcon.Pixels(icon, size) is { } pixels)
            {
                _looks[window] = look with { Icon = pixels };
                Draw();
            }
        }));
    }

    private void Draw()
    {
        if (_view is not { } view || !_window.IsShown && view.Place.Bounds.IsEmpty)
        {
            return;
        }

        var looks = view.Tabs.Select(tab => _looks.TryGetValue(tab, out var look) ? look : new TabLook(string.Empty, null)).ToList();
        var style = new TabStyle(_dpi, _textScale ??= WindowsTheme.TextScale(), OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Segoe UI Variable Text" : "Segoe UI", OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), _rightToLeft);
        var canvas = TabStripPainter.Paint(view, looks, _hover, _colors ??= ReadColors(), style);
        _window.Show(canvas.Pixels, view.Place.Bounds);
    }

    // The app mode, as apps' title bars follow it, the accent color, or high contrast.
    private static TabColors ReadColors()
    {
        if (WindowsTheme.IsHighContrast())
        {
            return TabColors.HighContrast(WindowsTheme.WindowColor(), WindowsTheme.TextColor(), WindowsTheme.HighlightColor());
        }

        return WindowsTheme.IsAppDark() ? TabColors.Dark(WindowsTheme.LightAccentColor()) : TabColors.Light(WindowsTheme.AccentColor());
    }

    private void OnPointer(WindowPointerEventArgs e)
    {
        if (_view is not { } view)
        {
            return;
        }

        var tab = view.Place.TabAt(e.Position);
        switch (e.Action)
        {
            case WindowPointerAction.Moved:
                if (_pressed >= 0 && !_dragging && Distance(e.Position, _pressedAt) > StripLayout.Scale(DragThreshold, _dpi))
                {
                    _dragging = true;
                }

                if (tab != _hover && !_dragging)
                {
                    _hover = tab;
                    _window.SetToolTip(tab >= 0 ? NameOf(tab) : string.Empty, _rightToLeft);
                    Draw();
                }

                break;

            case WindowPointerAction.LeftDown when tab >= 0:
                _pressed = tab;
                _pressedAt = e.Position;
                _window.CapturePointer();
                break;

            case WindowPointerAction.LeftUp:
                var pressed = _pressed;
                var dragged = _dragging;
                EndPress();
                if (pressed < 0)
                {
                    break;
                }

                if (dragged)
                {
                    TabDropped?.Invoke(this, new TabDrop(pressed, new Point(view.Place.Bounds.X + e.Position.X, view.Place.Bounds.Y + e.Position.Y)));
                }
                else if (tab == pressed)
                {
                    TabChosen?.Invoke(this, tab);
                }

                break;

            case WindowPointerAction.RightUp when tab >= 0:
                ShowMenu(tab, new Point(view.Place.Bounds.X + e.Position.X, view.Place.Bounds.Y + e.Position.Y));
                break;

            case WindowPointerAction.CaptureLost:
                _pressed = -1;
                _dragging = false;
                break;
        }
    }

    // Lets the pointer go after a press, before anything runs that may need it.
    private void EndPress()
    {
        _pressed = -1;
        _dragging = false;
        _window.ReleasePointer();
    }

    private void ShowMenu(int tab, Point at)
    {
        var chosen = PopupMenu.Show(
            _menuOwner,
            [
                new PopupMenuItem(MoveOutItem, Strings.WindowTabsMoveOutOfGroup),
                new PopupMenuItem(CloseItem, Strings.WindowTabsCloseWindow),
                PopupMenuItem.Separator,
                new PopupMenuItem(SettingsItem, Strings.WindowTabsSettings),
            ],
            at.X,
            at.Y,
            _rightToLeft);

        var item = chosen switch
        {
            MoveOutItem => TabMenuItem.MoveOutOfGroup,
            CloseItem => TabMenuItem.CloseWindow,
            SettingsItem => TabMenuItem.Settings,
            _ => (TabMenuItem?)null,
        };

        if (item is { } choice)
        {
            MenuChosen?.Invoke(this, new TabMenuChoice(tab, choice));
        }
    }

    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>The accent-colored highlight on a window's top while a window dragged with Ctrl held would group with it.</summary>
internal sealed class DropHighlight : IDropHighlight
{
    private readonly LayeredWindow _window = new("PCCompanion.WindowTabs.DropHighlight", Strings.WindowTabsName, clickThrough: true);
    private Rectangle _shown;

    public void Show(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            Hide();
            return;
        }

        if (area == _shown && _window.IsShown)
        {
            return;
        }

        var dark = WindowsTheme.IsAppDark();
        var accent = WindowsTheme.IsHighContrast() ? WindowsTheme.HighlightColor() : dark ? WindowsTheme.LightAccentColor() : WindowsTheme.AccentColor();
        var canvas = new StripCanvas(area.Width, area.Height);
        var corner = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? 8 : 0;
        const int Line = 2;
        canvas.Fill(0, 0, area.Width, area.Height, corner, accent, 90);
        canvas.Fill(0, 0, area.Width, Line, 0, accent, 255);
        canvas.Fill(0, area.Height - Line, area.Width, Line, 0, accent, 255);
        canvas.Fill(0, 0, Line, area.Height, 0, accent, 255);
        canvas.Fill(area.Width - Line, 0, Line, area.Height, 0, accent, 255);
        _window.Show(canvas.Pixels, area);
        _shown = area;
    }

    public void Hide() => _window.Hide();

    public void Dispose() => _window.Dispose();
}
