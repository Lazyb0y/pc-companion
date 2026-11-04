using System;

namespace PCCompanion.Core.Features;

/// <summary>
/// Shows notifications from the PC Companion icon, for features, so that each feature doesn't need
/// an icon of its own.
/// </summary>
public interface IHostNotifications
{
    /// <summary>
    /// Shows a notification from the PC Companion icon, which Windows shows as a toast and keeps in
    /// the notification center, unless the user turned off PC Companion's notifications or Do not
    /// disturb holds it back. Call it on the host's UI thread. Returns false when Windows refused,
    /// for example because the icon isn't shown yet.
    /// </summary>
    /// <param name="selected">
    /// Runs on the host's UI thread when the user selects the notification, so it answers at once
    /// and passes work that can wait to another thread. It mustn't throw. Windows says that a
    /// notification was selected, not which one, so a selection goes to the notification shown last,
    /// and once: selecting an older one, as in the notification center, after a later one was shown
    /// and selected does nothing.
    /// </param>
    bool Show(string title, string text, Action? selected);
}
