using System.Collections.Generic;

namespace PCCompanion.Core.Features;

/// <summary>
/// Implemented by a feature that adds items to the PC Companion icon's menu, such as a submenu,
/// above Settings and Exit. The host asks a running feature each time the menu opens.
/// </summary>
public interface IHostMenuItems
{
    /// <summary>
    /// The items to add, or none. Called on the host's UI thread each time the menu opens, so it
    /// answers at once from what the feature already knows, without reading files or the registry
    /// or waiting for other programs.
    /// </summary>
    IReadOnlyList<HostMenuItem> GetMenuItems();

    /// <summary>
    /// The user chose the item with <paramref name="id"/>. Called on the host's UI thread; work that
    /// can wait goes to the thread pool.
    /// </summary>
    void ChooseMenuItem(int id);
}
