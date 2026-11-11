using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows;

/// <summary>Logs what a <see cref="ShortcutKeeper"/> did, in the log of the feature it's for.</summary>
public static partial class ShortcutKeeperLog
{
    /// <summary>Logs the keeper's registrations from now on.</summary>
    /// <param name="feature">The feature's name for the log, such as "App Windows".</param>
    public static void Attach(ShortcutKeeper keeper, ILogger logger, string feature)
    {
        keeper.Registered += (_, registered) => LogRegistered(logger, feature, registered.Shortcut.Modifiers, registered.VirtualKey);
        keeper.Refused += (_, refused) => LogRefused(logger, feature, refused.Shortcut.Modifiers, refused.VirtualKey);
        keeper.NoKey += (_, shortcut) => LogNoKey(logger, feature, shortcut.Modifiers, shortcut.ScanCode);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Feature} registered {Modifiers} with virtual key {VirtualKey:X2}.")]
    private static partial void LogRegistered(ILogger logger, string feature, HotKeyModifiers modifiers, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused the {Feature} shortcut, {Modifiers} with virtual key {VirtualKey:X2}; another program may have it.")]
    private static partial void LogRefused(ILogger logger, string feature, HotKeyModifiers modifiers, uint virtualKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The key with scan code {ScanCode:X2} means no key in the layout in front, so {Feature} has no shortcut {Modifiers} with it there.")]
    private static partial void LogNoKey(ILogger logger, string feature, HotKeyModifiers modifiers, uint scanCode);
}
