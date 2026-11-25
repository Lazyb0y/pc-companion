using System;
using System.Drawing;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowShading;

/// <summary>
/// A rolled-up window's strip: a <see cref="ThumbnailWindow"/> showing the picture of its title
/// bar, which tells a click from a drag and moves itself while dragged. Create, use and dispose it
/// on the feature's thread.
/// </summary>
internal sealed class ShadeStrip : IShadeStrip
{
    // Effective pixels the pointer moves with the button held before a press is a drag.
    private const int DragThreshold = 4;

    private static int s_count;

    private readonly ThumbnailWindow _window;
    private readonly Action<Action> _guard;
    private readonly int _threshold;
    private Point? _pressedAt;
    private Point _startedAt;
    private bool _dragging;

    public ShadeStrip(nint window, Rectangle bounds, Rectangle source, bool topmost, string name, uint dpi, Action<Action> guard)
    {
        _guard = guard;
        _threshold = (int)Math.Round(DragThreshold * dpi / 96.0);
        _window = new ThumbnailWindow($"PCCompanion.WindowShading.Strip.{Interlocked.Increment(ref s_count)}", name, window, bounds, source, topmost);
        _window.PointerInput += (_, e) => _guard(() => OnPointer(e));
        _window.WheelTurned += (_, delta) => _guard(() => Wheeled?.Invoke(this, delta));
        _window.PointerOverChanged += (_, _) => _guard(() => (_window.IsPointerOver ? PointerEntered : PointerLeft)?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler<Point>? Clicked;

    public event EventHandler<int>? Wheeled;

    public event EventHandler<Point>? MenuRequested;

    public event EventHandler<Rectangle>? Moved;

    public event EventHandler? PointerEntered;

    public event EventHandler? PointerLeft;

    public Rectangle Bounds => _window.Bounds;

    public void SetTopmost(bool topmost) => _window.SetTopmost(topmost);

    public void Show() => _window.Show();

    public void Hide() => _window.Hide();

    public void Dispose() => _window.Dispose();

    // Positions come in the strip's pixels; while it moves with a drag, they're turned into the
    // screen's by its place at the time.
    private void OnPointer(WindowPointerEventArgs e)
    {
        var bounds = _window.Bounds;
        var onScreen = new Point(bounds.X + e.Position.X, bounds.Y + e.Position.Y);
        switch (e.Action)
        {
            case WindowPointerAction.LeftDown:
                _pressedAt = onScreen;
                _startedAt = bounds.Location;
                _dragging = false;
                _window.CapturePointer();
                break;

            case WindowPointerAction.Moved when _pressedAt is { } pressed:
                if (!_dragging && (Math.Abs(onScreen.X - pressed.X) > _threshold || Math.Abs(onScreen.Y - pressed.Y) > _threshold))
                {
                    _dragging = true;
                }

                if (_dragging)
                {
                    _window.MoveTo(new Point(_startedAt.X + onScreen.X - pressed.X, _startedAt.Y + onScreen.Y - pressed.Y));
                }

                break;

            case WindowPointerAction.LeftUp when _pressedAt is not null:
                var dragged = _dragging;
                EndPress();
                if (dragged)
                {
                    Moved?.Invoke(this, _window.Bounds);
                }
                else
                {
                    Clicked?.Invoke(this, e.Position);
                }

                break;

            case WindowPointerAction.CaptureLost when _pressedAt is not null:
                var moved = _dragging;
                EndPress();
                if (moved)
                {
                    Moved?.Invoke(this, _window.Bounds);
                }

                break;

            case WindowPointerAction.RightUp:
                MenuRequested?.Invoke(this, onScreen);
                break;
        }
    }

    private void EndPress()
    {
        _pressedAt = null;
        _dragging = false;
        _window.ReleasePointer();
    }
}
