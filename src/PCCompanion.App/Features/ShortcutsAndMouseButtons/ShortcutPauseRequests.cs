using System;
using Microsoft.UI.Dispatching;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.ShortcutsAndMouseButtons;

/// <summary>
/// Asks the host to pause PC Companion's shortcuts while the editor's box listens, and again every
/// 5 seconds, since each request lasts 10; then to resume them. A host that doesn't answer, such as
/// one that isn't running, changes nothing. Use it on the UI thread.
/// </summary>
internal sealed class ShortcutPauseRequests
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer? _timer;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = Every;
        _timer.Tick += (_, _) => Ask(pause: true);
        _timer.Start();
        Ask(pause: true);
    }

    public void Stop()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer = null;
        Ask(pause: false);
    }

    private static void Ask(bool pause) => _ = ShortcutPause.AskAsync(pause);
}
