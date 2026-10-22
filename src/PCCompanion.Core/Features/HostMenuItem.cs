using System;
using System.Collections.Generic;

namespace PCCompanion.Core.Features;

/// <summary>
/// An item a feature adds to the PC Companion icon's menu, through <see cref="IHostMenuItems"/>.
/// </summary>
/// <param name="Id">
/// The feature's own ID for the item, which <see cref="IHostMenuItems.ChooseMenuItem"/> gets back.
/// 0 for <see cref="Separator"/>, a <see cref="Submenu"/>, and an item that can't be chosen, which
/// shows dimmed.
/// </param>
/// <param name="Text">Shown as written: the host shows a <c>&amp;</c> as itself, not as an access key.</param>
public sealed record HostMenuItem(int Id, string Text)
{
    /// <summary>A line between groups of items.</summary>
    public static HostMenuItem Separator { get; } = new(0, string.Empty) { IsSeparator = true };

    /// <summary>Whether the item can be chosen. A disabled item shows dimmed, as a heading or a note.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Shows a radio mark before the item, as for the chosen one of a group.</summary>
    public bool IsChecked { get; init; }

    public bool IsSeparator { get; private init; }

    /// <summary>The items of a <see cref="Submenu"/>; null for other items.</summary>
    public IReadOnlyList<HostMenuItem>? Items { get; private init; }

    /// <summary>An item that opens a submenu of <paramref name="items"/>, instead of being chosen.</summary>
    public static HostMenuItem Submenu(string text, IReadOnlyList<HostMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new HostMenuItem(0, text) { Items = items };
    }
}
