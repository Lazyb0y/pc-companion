using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <param name="Id">Returned by <see cref="PopupMenu.Show"/> when the item is chosen; must not be 0.</param>
public sealed record PopupMenuItem(uint Id, string Text);

/// <summary>
/// A Windows shortcut menu, as shown for tray icons.
/// </summary>
public static class PopupMenu
{
    /// <summary>
    /// Shows the menu at a screen point and returns the ID of the chosen item, or 0 when the
    /// menu was closed without a choice.
    /// </summary>
    /// <param name="owner">The window that owns the menu while it is open.</param>
    /// <param name="rightToLeft">Lays out the menu from right to left.</param>
    public static uint Show(MessageWindow owner, IReadOnlyList<PopupMenuItem> items, int x, int y, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(items);

        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        foreach (var item in items)
        {
            if (!PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, item.Id, item.Text))
            {
                throw new Win32Exception();
            }
        }

        var flags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON;
        flags |= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_MENUDROPALIGNMENT) != 0
            ? TRACK_POPUP_MENU_FLAGS.TPM_RIGHTALIGN
            : TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN;
        if (rightToLeft)
        {
            flags |= TRACK_POPUP_MENU_FLAGS.TPM_LAYOUTRTL;
        }

        // The owner must be the foreground window, or the menu stays open when the user
        // clicks elsewhere; the posted message lets the menu close properly.
        var window = (HWND)owner.Handle;
        PInvoke.SetForegroundWindow(window);
        var chosen = PInvoke.TrackPopupMenuEx(menu, (uint)flags, x, y, window, null);
        PInvoke.PostMessage(window, PInvoke.WM_NULL, 0, 0);

        return (uint)chosen.Value;
    }
}
