using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <param name="Id">Returned by <see cref="PopupMenu.Show"/> when the item is chosen; must not be 0, except for <see cref="Separator"/>.</param>
public sealed record PopupMenuItem(uint Id, string Text)
{
    /// <summary>A line between groups of items.</summary>
    public static PopupMenuItem Separator { get; } = new(0, string.Empty) { IsSeparator = true };

    /// <summary>Whether the item can be chosen. A disabled item shows dimmed, as a heading or a note.</summary>
    public bool IsEnabled { get; init; } = true;

    public bool IsSeparator { get; private init; }
}

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
    public static uint Show(MessageWindow owner, IReadOnlyList<PopupMenuItem> items, int x, int y, bool rightToLeft) =>
        Show(owner, items, x, y, rightToLeft, dropWaitingKeys: false);

    /// <summary>
    /// Shows the menu for a keyboard shortcut the user pressed, at a screen point, and returns the
    /// ID of the chosen item, or 0 when the menu was closed without a choice. Key messages already
    /// waiting for this thread are dropped first: while the owner is the front window, such as
    /// after an earlier menu, the shortcut's own Alt key waits there and would close the menu at
    /// once. When the menu closes without a choice and the owner is still the front window, the
    /// window that was in front before gets the front back, so typing goes on there.
    /// </summary>
    /// <param name="owner">The window that owns the menu while it is open.</param>
    /// <param name="rightToLeft">Lays out the menu from right to left.</param>
    public static uint ShowForShortcut(MessageWindow owner, IReadOnlyList<PopupMenuItem> items, int x, int y, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var previous = PInvoke.GetForegroundWindow();
        var chosen = Show(owner, items, x, y, rightToLeft, dropWaitingKeys: true);

        var window = (HWND)owner.Handle;
        if (chosen == 0 && !previous.IsNull && previous != window && PInvoke.GetForegroundWindow() == window && PInvoke.IsWindow(previous))
        {
            PInvoke.SetForegroundWindow(previous);
        }

        return chosen;
    }

    /// <summary>
    /// Drops the keyboard messages waiting for the calling thread and returns how many there were.
    /// </summary>
    public static int DropWaitingKeys()
    {
        var count = 0;
        while (PInvoke.PeekMessage(out _, HWND.Null, PInvoke.WM_KEYFIRST, PInvoke.WM_KEYLAST, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Doubles each <c>&amp;</c> in <paramref name="text"/>, so an item shows it as written instead
    /// of underlining the next letter as the item's access key.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Replace("&", "&&", StringComparison.Ordinal);
    }

    private static uint Show(MessageWindow owner, IReadOnlyList<PopupMenuItem> items, int x, int y, bool rightToLeft, bool dropWaitingKeys)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(items);

        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        foreach (var item in items)
        {
            var itemFlags = item.IsSeparator ? MENU_ITEM_FLAGS.MF_SEPARATOR
                : item.IsEnabled ? MENU_ITEM_FLAGS.MF_STRING
                : MENU_ITEM_FLAGS.MF_STRING | MENU_ITEM_FLAGS.MF_GRAYED;
            if (!PInvoke.AppendMenu(menu, itemFlags, item.Id, item.IsSeparator ? null : item.Text))
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
        if (dropWaitingKeys)
        {
            DropWaitingKeys();
        }

        var chosen = PInvoke.TrackPopupMenuEx(menu, (uint)flags, x, y, window, null);
        PInvoke.PostMessage(window, PInvoke.WM_NULL, 0, 0);

        return (uint)chosen.Value;
    }
}
