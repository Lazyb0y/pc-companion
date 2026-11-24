using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.WindowTabs;

/// <summary>
/// Window Tabs settings, stored in <c>features.windowTabs</c>. See docs/features/windows/window-tabs.md.
/// </summary>
public sealed record WindowTabsSettings : IFeatureSettings<WindowTabsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>Whether dropping a window with Ctrl held on another window's title bar groups the two.</summary>
    public bool GroupByDropping { get; set; } = true;

    /// <summary>
    /// The shortcuts that show the next and previous tabs, with Shift to move the tab in front. A
    /// number in the file reads as any value, named or not; an unknown one registers none.
    /// </summary>
    public TabSwitchShortcuts SwitchShortcuts { get; set; } = TabSwitchShortcuts.WindowsControlPage;

    /// <summary>Reads the feature's section, or returns the defaults when there is none.</summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static WindowTabsSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, WindowTabsJsonContext.Default.WindowTabsSettings, () => new WindowTabsSettings());

    public JsonElement Write() => FeatureSettings.Write(this, WindowTabsJsonContext.Default.WindowTabsSettings);

    public WindowTabsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}

/// <summary>
/// The shortcuts that switch tabs: the key with Page Down for the next tab and Page Up for the
/// previous one, and with Shift to move the tab in front. The names are stored in the settings
/// file, so never rename one.
/// </summary>
public enum TabSwitchShortcuts
{
    /// <summary>No shortcuts.</summary>
    None,

    /// <summary>The Windows key and Ctrl with Page Down and Page Up.</summary>
    WindowsControlPage,

    /// <summary>The Windows key and Alt with Page Down and Page Up.</summary>
    WindowsAltPage,
}
