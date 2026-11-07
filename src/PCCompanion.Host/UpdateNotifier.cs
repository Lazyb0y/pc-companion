using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Runs the update check while the host runs and shows a notification from the tray icon when a
/// newer version is out; selecting it opens the release page. While the version is out, the tray
/// menu has an item that opens it too, and until it's seen, the icon has a dot. See
/// docs/features/general/update-check.md.
/// </summary>
internal sealed partial class UpdateNotifier : IDisposable
{

    // The copy's check has 30 seconds for GitHub's answer, after the copy starts.
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(1);

    private readonly UpdateChecker _checker;
    private readonly UpdateCheckStore _store;
    private readonly AppDataPaths _paths;
    private readonly UpdateCheckScheduler _scheduler;
    private readonly IHostNotifications _notifications;
    private readonly ILogger<UpdateNotifier> _logger;
    private readonly ILogger<UpdateCheckWatcher> _watcherLogger;
    private readonly Lock _lock = new();
    private MessageWindow? _window;
    private TrayController? _tray;

    // Created on the thread pool, since it reads the result.
    private UpdateCheckWatcher? _watcher;
    private bool _disposed;

    // Used on the UI thread.
    private bool _checkAutomatically;
    private ReleaseVersion? _dotVersion;

    public UpdateNotifier(UpdateChecker checker, UpdateCheckStore store, AppDataPaths paths, IHostNotifications notifications, TimeProvider time, ILogger<UpdateNotifier> logger, ILogger<UpdateCheckScheduler> schedulerLogger, ILogger<UpdateCheckWatcher> watcherLogger)
    {
        _checker = checker;
        _store = store;
        _paths = paths;
        _notifications = notifications;
        _logger = logger;
        _watcherLogger = watcherLogger;
        _scheduler = new UpdateCheckScheduler(CheckAsync, store, time, schedulerLogger);

        // Found on the thread pool; the tray icon is used on the window's thread.
        _scheduler.UpdateFound += (_, e) => _window?.BeginInvoke(() => ShowNotification(e.Version));
    }

    /// <summary>The running version.</summary>
    public static ReleaseVersion CurrentVersion { get; } =
        ReleaseVersion.FromAssemblyVersion(typeof(UpdateNotifier).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Shows the notifications, the tray menu's update item and the icon's dot on the host's
    /// window's thread. Call it on that thread, once the tray icon is shown.
    /// </summary>
    public void Attach(MessageWindow window, TrayController tray)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(tray);

        _window = window;
        _tray = tray;
        tray.UpdateChosen += (_, e) => OpenChosenUpdate(e.Version);

        // The UI thread, which every feature shares, doesn't wait for the disk.
        _ = Task.Run(WatchResult);
    }

    /// <summary>
    /// Starts or stops the checks, and shows or hides the update in the tray; call it at startup
    /// and after every settings change, on the UI thread.
    /// </summary>
    public void Apply(UpdateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _checkAutomatically = settings.CheckAutomatically;
        _scheduler.Apply(settings.CheckAutomatically);

        // Also shows the tooltip and the menu's item in the host's language, which may have changed.
        ShowInTray();
    }

    public void Dispose()
    {
        _scheduler.Dispose();
        UpdateCheckWatcher? watcher;
        lock (_lock)
        {
            _disposed = true;
            watcher = _watcher;
        }

        watcher?.Dispose();
    }

    // On the thread pool. The result is read as the watcher starts, and again after every save.
    private void WatchResult()
    {
        UpdateCheckWatcher watcher;
        try
        {
            watcher = new UpdateCheckWatcher(_store, _paths, _watcherLogger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantWatch(_logger, exception);
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                watcher.Dispose();
                return;
            }

            _watcher = watcher;
        }

        watcher.Changed += (_, _) => _window?.BeginInvoke(ShowInTray);
        watcher.Seen += (_, _) => _window?.BeginInvoke(ShowInTray);
        _window?.BeginInvoke(ShowInTray);
    }

    // On the UI thread.
    private void ShowInTray()
    {
        UpdateCheckWatcher? watcher;
        lock (_lock)
        {
            watcher = _watcher;
        }

        if (_tray is null || watcher is null)
        {
            return;
        }

        var (update, unseen) = watcher.ForTrayIcon(_checkAutomatically, CurrentVersion);
        if (unseen != _dotVersion)
        {
            if (unseen is { } version)
            {
                LogDotShown(_logger, version);
            }
            else
            {
                LogDotGone(_logger);
            }

            _dotVersion = unseen;
        }

        _tray.ShowUpdate(update, unseen is not null);
    }

    // On the UI thread. Choosing the item counts as seeing the version, as the General page
    // showing it does, so the dot and the General item's badge go away.
    private void OpenChosenUpdate(ReleaseVersion version)
    {
        LogUpdateChosen(_logger, version);
        UpdateCheckWatcher? watcher;
        lock (_lock)
        {
            watcher = _watcher;
        }

        // Saving reads and writes the file, and a host running as administrator waits for its copy
        // to open the page. Such a host never saves to the user's folder: the dot then stays away
        // until it stops.
        _ = Task.Run(() =>
        {
            watcher?.MarkSeen(version, save: !Elevation.IsUacElevated);
            OpenReleasePage();
        });
    }

    // A host elevated through UAC leaves the check to a copy without admin rights; see
    // UpdateCheckCommand. With UAC off, every program already has admin rights.
    private Task<UpdateCheckOutcome> CheckAsync(CancellationToken cancellationToken) =>
        Elevation.IsUacElevated ? CheckInCopyAsync(cancellationToken) : _checker.CheckAsync(CurrentVersion, cancellationToken);

    private async Task<UpdateCheckOutcome> CheckInCopyAsync(CancellationToken cancellationToken)
    {
        var before = _store.Load();
        LogCheckingInCopy(_logger);
        await Task.Run(() => UnelevatedProcess.Run(HostProcess.ExecutablePath, HostProcess.CheckForUpdatesArgument, CopyTimeout), cancellationToken).ConfigureAwait(false);
        return UpdateChecker.OutcomeOfCopy(before, _store.Load(), CurrentVersion);
    }

    private void ShowNotification(ReleaseVersion version)
    {
        // Parsed each time: the host's language can change while it runs.
        var title = string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.UpdateNotificationTitle), version);
        if (_notifications.Show(title, Strings.UpdateNotificationMessage, () => Task.Run(() =>
        {
            LogSelected(_logger);
            OpenReleasePage();
        })))
        {
            LogShown(_logger, version);
        }
        else
        {
            LogNotShown(_logger, version);
        }
    }

    // Like a feature's web page, never with admin rights: see WebPageCommand. On the thread pool,
    // since a host running as administrator waits for its copy to open it.
    private void OpenReleasePage()
    {
        if (!WebPageCommand.Open(GitHubReleaseSource.ReleasePageAddress))
        {
            LogCantOpen(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Running the update check in a copy of the host without admin rights.")]
    private static partial void LogCheckingInCopy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Showed the update notification for PC Companion {Version}.")]
    private static partial void LogShown(ILogger logger, ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't show the update notification for PC Companion {Version}; the tray icon may not be shown.")]
    private static partial void LogNotShown(ILogger logger, ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The update notification was selected; opening the release page.")]
    private static partial void LogSelected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't open the release page.")]
    private static partial void LogCantOpen(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The tray icon shows the update dot for PC Companion {Version}.")]
    private static partial void LogDotShown(ILogger logger, ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The update dot on the tray icon went away.")]
    private static partial void LogDotGone(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The tray menu's item for PC Companion {Version} was chosen; opening the release page.")]
    private static partial void LogUpdateChosen(ILogger logger, ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't watch the update check's result, so the tray icon shows no update.")]
    private static partial void LogCantWatch(ILogger logger, Exception exception);
}
