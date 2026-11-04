using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Infrastructure.Features;

/// <summary>
/// Shows the features' notifications, and the host's own, from the PC Companion icon, and passes a
/// selection to whoever showed the notification last. The host attaches its icon once it's shown; a
/// notification shown before that is logged and dropped. Use it on the host's UI thread.
/// </summary>
public sealed partial class HostNotifications(ILogger<HostNotifications> logger) : IHostNotifications
{
    private Func<string, string, bool>? _show;
    private Action? _selected;

    /// <param name="show">Shows a notification from the icon, and returns false when Windows refused.</param>
    public void Attach(Func<string, string, bool> show)
    {
        ArgumentNullException.ThrowIfNull(show);

        _show = show;
    }

    public bool Show(string title, string text, Action? selected)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);

        if (_show is not { } show)
        {
            LogNotAttached(logger);
            return false;
        }

        if (!show(title, text))
        {
            return false;
        }

        // Windows reports a selection without saying which notification, so it goes to this one.
        _selected = selected;
        return true;
    }

    /// <summary>The user selected a notification from the icon; call it on the host's UI thread.</summary>
    public void Selected()
    {
        var selected = _selected;
        _selected = null;
        if (selected is null)
        {
            LogNothingSelected(logger);
            return;
        }

        try
        {
            selected();
        }
        catch (Exception exception)
        {
            LogSelectionFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A notification was shown before the host's icon was ready, so nothing showed.")]
    private static partial void LogNotAttached(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A notification was selected that has nothing to do, such as one selected before.")]
    private static partial void LogNothingSelected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a selected notification failed.")]
    private static partial void LogSelectionFailed(ILogger logger, Exception exception);
}
