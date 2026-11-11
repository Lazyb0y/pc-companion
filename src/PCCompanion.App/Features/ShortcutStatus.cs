using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features;

/// <summary>
/// Which of a feature's shortcuts Windows refused, as the host last said through its pipe, for the
/// feature's page: read now and every 2 seconds while the page shows, off the UI thread. While the
/// feature is off, or the host doesn't answer, none is refused. See "Running in the host" in
/// docs/architecture/features.md.
/// </summary>
/// <param name="feature">The feature's ID.</param>
/// <param name="isOn">Whether the feature is on, asked on the page's thread before each read.</param>
public sealed class ShortcutStatus(string feature, Func<bool> isOn)
{
    // How long the page waits for the host to answer.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);
    private static readonly CompositeFormat RefusedFormat = CompositeFormat.Parse(Strings.ShortcutRefused);

    private DispatcherQueueTimer? _timer;
    private bool _reading;

    /// <summary>Raised on the page's thread when <see cref="Refused"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The feature's names of the shortcuts Windows refused.</summary>
    public IReadOnlySet<string> Refused { get; private set; } = None;

    /// <summary>The warning note at a refused shortcut, such as "Another program uses Ctrl + Alt + Esc, so this shortcut does nothing here."</summary>
    /// <param name="keys">The shortcut as the page shows it.</param>
    public static string RefusedNote(string keys) => string.Format(CultureInfo.CurrentCulture, RefusedFormat, keys);

    /// <summary>Starts reading, now and every 2 seconds, until <see cref="Stop"/>. Call it on the page's thread.</summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    // A read still going when the next tick comes is let finish.
    private async Task RefreshAsync()
    {
        if (_reading)
        {
            return;
        }

        _reading = true;
        try
        {
            var snapshot = isOn()
                ? await Task.Run(() => SnapshotPipe.ReadAsync(ShortcutRefusals.PipeName, ReadTimeout, ShortcutRefusals.MaximumSnapshotLength)).ConfigureAwait(true)
                : null;
            var all = snapshot is null ? null : ShortcutRefusals.FromJson(snapshot);
            var refused = all is null ? None : ShortcutRefusals.Of(all, feature);
            if (!refused.SetEquals(Refused))
            {
                Refused = refused;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _reading = false;
        }
    }
}
