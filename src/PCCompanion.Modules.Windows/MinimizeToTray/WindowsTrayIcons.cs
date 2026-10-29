using System;
using System.Collections.Generic;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// The apps' notification icons on the feature thread's window, which Windows adds again when the
/// taskbar restarts. Use it on that thread.
/// </summary>
public sealed class WindowsTrayIcons(MessageWindow window) : ITrayIcons, IDisposable
{
    private readonly Dictionary<uint, TrayIcon> _icons = [];

    /// <summary>Raised when the user selects an icon, with its ID.</summary>
    public event EventHandler<uint>? Selected;

    /// <summary>Raised when the user asks for an icon's menu, with its ID and the screen point to show it at.</summary>
    public event EventHandler<(uint Id, TrayMenuEventArgs Point)>? MenuRequested;

    public bool Show(uint id, uint[] pixels, int size, string name, string toolTip)
    {
        var image = IconImage.FromPixels(pixels, size);
        if (!_icons.TryGetValue(id, out var icon))
        {
            try
            {
                // Added with the app's name, which Windows and screen readers keep as the icon's name.
                icon = new TrayIcon(window, id, image, name);
            }
            catch
            {
                image.Dispose();
                throw;
            }

            icon.Selected += (_, _) => Selected?.Invoke(this, id);
            icon.MenuRequested += (_, e) => MenuRequested?.Invoke(this, (id, e));
            _icons[id] = icon;
        }

        return icon.Update(image, toolTip);
    }

    public void Remove(uint id)
    {
        if (_icons.Remove(id, out var icon))
        {
            icon.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }
}
