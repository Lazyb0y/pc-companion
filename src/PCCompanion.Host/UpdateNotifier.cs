using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Runs the update check while the host runs and shows a notification from the tray icon when a
/// newer version is out; selecting it opens the release page. See
/// docs/features/general/update-check.md.
/// </summary>
internal sealed partial class UpdateNotifier : IDisposable
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, HostProcess.FileName);

    // The copy's check has 30 seconds for GitHub's answer, after the copy starts.
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(1);

    private readonly UpdateChecker _checker;
    private readonly UpdateCheckStore _store;
    private readonly UpdateCheckScheduler _scheduler;
    private readonly ILogger<UpdateNotifier> _logger;
    private MessageWindow? _window;
    private TrayController? _tray;

    public UpdateNotifier(UpdateChecker checker, UpdateCheckStore store, TimeProvider time, ILogger<UpdateNotifier> logger, ILogger<UpdateCheckScheduler> schedulerLogger)
    {
        _checker = checker;
        _store = store;
        _logger = logger;
        _scheduler = new UpdateCheckScheduler(CheckAsync, store, time, schedulerLogger);

        // Found on the thread pool; the tray icon is used on the window's thread.
        _scheduler.UpdateFound += (_, e) => _window?.BeginInvoke(() => ShowNotification(e.Version));
    }

    /// <summary>The running version.</summary>
    public static ReleaseVersion CurrentVersion { get; } =
        ReleaseVersion.FromAssemblyVersion(typeof(UpdateNotifier).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Shows the notifications from the host's tray icon. Call it on the window's thread, once the
    /// icon is shown.
    /// </summary>
    public void Attach(MessageWindow window, TrayController tray)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(tray);

        _window = window;
        _tray = tray;
        tray.NotificationSelected += (_, _) => OpenReleasePage();
    }

    /// <summary>Starts or stops the checks; call it at startup and after every settings change.</summary>
    public void Apply(UpdateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _scheduler.Apply(settings.CheckAutomatically);
    }

    public void Dispose() => _scheduler.Dispose();

    // A host elevated through UAC leaves the check to a copy without admin rights; see
    // UpdateCheckCommand. With UAC off, every program already has admin rights.
    private Task<UpdateCheckOutcome> CheckAsync(CancellationToken cancellationToken) =>
        Elevation.IsUacElevated ? CheckInCopyAsync(cancellationToken) : _checker.CheckAsync(CurrentVersion, cancellationToken);

    private async Task<UpdateCheckOutcome> CheckInCopyAsync(CancellationToken cancellationToken)
    {
        var before = _store.Load();
        LogCheckingInCopy(_logger);
        await Task.Run(() => UnelevatedProcess.Run(HostPath, HostProcess.CheckForUpdatesArgument, CopyTimeout), cancellationToken).ConfigureAwait(false);
        return UpdateChecker.OutcomeOfCopy(before, _store.Load(), CurrentVersion);
    }

    private void ShowNotification(ReleaseVersion version)
    {
        // Parsed each time: the host's language can change while it runs.
        var title = string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(Strings.UpdateNotificationTitle), version);
        if (_tray?.ShowNotification(title, Strings.UpdateNotificationMessage) == true)
        {
            LogShown(_logger, version);
        }
        else
        {
            LogNotShown(_logger, version);
        }
    }

    // A browser started by a host running as administrator would run as administrator too, so
    // such a host has a copy without admin rights open the page.
    private void OpenReleasePage()
    {
        LogSelected(_logger);
        try
        {
            if (Elevation.IsUacElevated)
            {
                UnelevatedProcess.Start(HostPath, HostProcess.OpenReleasePageArgument);
            }
            else if (!ShellActions.OpenWebPage(GitHubReleaseSource.ReleasePageAddress))
            {
                LogCantOpen(_logger, null);
            }
        }
        catch (Win32Exception exception)
        {
            LogCantOpen(_logger, exception);
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
    private static partial void LogCantOpen(ILogger logger, Exception? exception);
}
