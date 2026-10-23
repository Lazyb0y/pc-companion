using System.Collections.Generic;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Input.HotCorners;

/// <summary>
/// The actions in the order the Hot Corners page lists them, and their names in the user interface
/// language, for the page and its recent results.
/// </summary>
public static class HotCornerActionNames
{
    /// <summary>Every action once, in the page's order.</summary>
    public static IReadOnlyList<HotCornerAction> InPageOrder { get; } =
    [
        HotCornerAction.None,
        HotCornerAction.TaskView,
        HotCornerAction.ShowDesktop,
        HotCornerAction.Start,
        HotCornerAction.Search,
        HotCornerAction.Notifications,
        HotCornerAction.Lock,
        HotCornerAction.TurnOffDisplays,
        HotCornerAction.ScreenSaver,
        HotCornerAction.KeepDisplaysOn,
        HotCornerAction.RunProgram,
    ];

    public static string NameOf(HotCornerAction action) => action switch
    {
        HotCornerAction.TaskView => Strings.HotCornersActionTaskView,
        HotCornerAction.ShowDesktop => Strings.HotCornersActionShowDesktop,
        HotCornerAction.Start => Strings.HotCornersActionStart,
        HotCornerAction.Search => Strings.HotCornersActionSearch,
        HotCornerAction.Notifications => Strings.HotCornersActionNotifications,
        HotCornerAction.Lock => Strings.HotCornersActionLock,
        HotCornerAction.TurnOffDisplays => Strings.HotCornersActionTurnOffDisplays,
        HotCornerAction.ScreenSaver => Strings.HotCornersActionScreenSaver,
        HotCornerAction.KeepDisplaysOn => Strings.HotCornersActionKeepDisplaysOn,
        HotCornerAction.RunProgram => Strings.HotCornersActionRunProgram,
        _ => Strings.HotCornersActionNone,
    };
}
