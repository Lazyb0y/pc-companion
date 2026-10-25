using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <param name="Id">
/// Returned by <see cref="PopupMenu.Show"/> when the item is chosen; must not be 0, except for
/// <see cref="Separator"/> and a <see cref="Submenu"/>. Items in submenus need IDs of their own too.
/// </param>
public sealed record PopupMenuItem(uint Id, string Text)
{
    /// <summary>A line between groups of items.</summary>
    public static PopupMenuItem Separator { get; } = new(0, string.Empty) { IsSeparator = true };

    /// <summary>Whether the item can be chosen. A disabled item shows dimmed, as a heading or a note.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Shows a radio mark (a dot) before the item, as for the chosen one of a group.</summary>
    public bool IsChecked { get; init; }

    public bool IsSeparator { get; private init; }

    /// <summary>The items of a <see cref="Submenu"/>; null for other items.</summary>
    public IReadOnlyList<PopupMenuItem>? Items { get; private init; }

    /// <summary>An item that opens a submenu of <paramref name="items"/>, instead of being chosen.</summary>
    public static PopupMenuItem Submenu(string text, IReadOnlyList<PopupMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new PopupMenuItem(0, text) { Items = items };
    }
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
        Show(owner, items, x, y, rightToLeft, keepClear: null, dropWaitingKeys: false);

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
    /// <param name="keepClear">
    /// A screen rectangle the menu doesn't cover, such as the line with the text cursor: the menu
    /// moves below or above it, on the display that has the point.
    /// </param>
    public static uint ShowForShortcut(MessageWindow owner, IReadOnlyList<PopupMenuItem> items, int x, int y, bool rightToLeft, Rectangle? keepClear = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var previous = Foreground.Window;
        var chosen = Show(owner, items, x, y, rightToLeft, keepClear, dropWaitingKeys: true);
        if (chosen == 0)
        {
            Foreground.GiveBack(owner.Handle, previous);
        }

        return chosen;
    }

    /// <summary>
    /// Closes the menu open on the calling thread, if there is one, as if the user pressed Esc.
    /// </summary>
    public static void CloseOpenMenu() => PInvoke.EndMenu();

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

    private static unsafe uint Show(MessageWindow owner, IReadOnlyList<PopupMenuItem> items, int x, int y, bool rightToLeft, Rectangle? keepClear, bool dropWaitingKeys)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(items);

        using var menu = Create(items);

        var flags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON;
        flags |= PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_MENUDROPALIGNMENT) != 0
            ? TRACK_POPUP_MENU_FLAGS.TPM_RIGHTALIGN
            : TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN;
        if (rightToLeft)
        {
            flags |= TRACK_POPUP_MENU_FLAGS.TPM_LAYOUTRTL;
        }

        // Windows moves the menu up or down, rather than sideways, to keep the rectangle clear.
        TPMPARAMS? parameters = null;
        if (keepClear is { } clear)
        {
            flags |= TRACK_POPUP_MENU_FLAGS.TPM_VERTICAL;
            parameters = new TPMPARAMS
            {
                cbSize = (uint)sizeof(TPMPARAMS),
                rcExclude = new RECT { left = clear.Left, top = clear.Top, right = clear.Right, bottom = clear.Bottom },
            };
        }

        // The owner must be the foreground window, or the menu stays open when the user
        // clicks elsewhere; the posted message lets the menu close properly.
        var window = (HWND)owner.Handle;
        PInvoke.SetForegroundWindow(window);
        if (dropWaitingKeys)
        {
            DropWaitingKeys();
        }

        // The menu runs a loop of its own until it closes, which isn't the handler's time.
        int chosen;
        using (SlowHandlerWatch.EnterModal())
        {
            chosen = PInvoke.TrackPopupMenuEx(menu, (uint)flags, x, y, window, parameters).Value;
        }

        PInvoke.PostMessage(window, PInvoke.WM_NULL, 0, 0);

        return (uint)chosen;
    }

    // A menu with the items, and their submenus, which Windows destroys with it.
    private static DestroyMenuSafeHandle Create(IReadOnlyList<PopupMenuItem> items)
    {
        var menu = PInvoke.CreatePopupMenu_SafeHandle();
        try
        {
            foreach (var item in items)
            {
                Append(menu, item);
            }

            return menu;
        }
        catch
        {
            menu.Dispose();
            throw;
        }
    }

    private static unsafe void Append(DestroyMenuSafeHandle menu, PopupMenuItem item)
    {
        var flags = item.IsSeparator ? MENU_ITEM_FLAGS.MF_SEPARATOR : MENU_ITEM_FLAGS.MF_STRING;
        if (!item.IsEnabled)
        {
            flags |= MENU_ITEM_FLAGS.MF_GRAYED;
        }

        if (item.IsChecked)
        {
            flags |= MENU_ITEM_FLAGS.MF_CHECKED;
        }

        if (item.Items is { } submenuItems)
        {
            using var submenu = Create(submenuItems);
            if (!PInvoke.AppendMenu(menu, flags | MENU_ITEM_FLAGS.MF_POPUP, (nuint)submenu.DangerousGetHandle(), item.Text))
            {
                throw new Win32Exception();
            }

            // The menu owns the submenu now, and destroys it with itself.
            submenu.SetHandleAsInvalid();
            return;
        }

        if (!PInvoke.AppendMenu(menu, flags, item.Id, item.IsSeparator ? null : item.Text))
        {
            throw new Win32Exception();
        }

        if (item.IsChecked)
        {
            // A dot instead of a check mark.
            var info = new MENUITEMINFOW { cbSize = (uint)sizeof(MENUITEMINFOW), fMask = MENU_ITEM_MASK.MIIM_FTYPE, fType = MENU_ITEM_TYPE.MFT_RADIOCHECK };
            if (!PInvoke.SetMenuItemInfo(menu, (uint)(PInvoke.GetMenuItemCount(menu) - 1), true, info))
            {
                throw new Win32Exception();
            }
        }
    }
}
