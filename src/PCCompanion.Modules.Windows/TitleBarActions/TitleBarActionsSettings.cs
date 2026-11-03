using System.Text.Json;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Title Bar Actions settings, stored in <c>features.titleBarActions</c>. See docs/features/windows/title-bar-actions.md.
/// </summary>
public sealed record TitleBarActionsSettings : IFeatureSettings<TitleBarActionsSettings>
{
    public bool Enabled { get; set; }

    /// <summary>
    /// What a middle-click on a title bar does. A number in the file reads as any value, named or
    /// not; an unknown one does nothing.
    /// </summary>
    public TitleBarClickAction MiddleClick { get; set; } = TitleBarClickAction.Minimize;

    public TitleBarClickAction ShiftMiddleClick { get; set; }

    public TitleBarClickAction ControlMiddleClick { get; set; }

    /// <summary>What the wheel on a title bar does.</summary>
    public TitleBarWheelAction Wheel { get; set; }

    public TitleBarWheelAction ControlWheel { get; set; }

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static TitleBarActionsSettings Read(JsonElement? section) =>
        FeatureSettings.Read(section, TitleBarActionsJsonContext.Default.TitleBarActionsSettings, () => new TitleBarActionsSettings());

    public JsonElement Write() => FeatureSettings.Write(this, TitleBarActionsJsonContext.Default.TitleBarActionsSettings);

    public TitleBarActionsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };
}

/// <summary>What a middle-click on a title bar does.</summary>
public enum TitleBarClickAction
{
    /// <summary>Nothing: the click reaches the app.</summary>
    None,

    Minimize,

    /// <summary>Puts the window behind every other window.</summary>
    SendToBack,

    /// <summary>Sends the window to the notification area, through Minimize to Tray.</summary>
    MinimizeToTray,

    /// <summary>Minimizes every window of the window's app, through Hide Apps.</summary>
    HideApp,
}

/// <summary>What the wheel on a title bar does.</summary>
public enum TitleBarWheelAction
{
    /// <summary>Nothing: the wheel reaches the app.</summary>
    None,

    /// <summary>Changes the window's opacity.</summary>
    Opacity,
}
