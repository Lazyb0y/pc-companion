using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Runs the update check for a host running as administrator, which starts the host with
/// <see cref="HostProcess.CheckForUpdatesArgument"/> without admin rights. The request, reading
/// the answer and saving the result in the user's folder never run with them.
/// </summary>
internal static partial class UpdateCheckCommand
{
    public static void Run()
    {
        // Started with admin rights, for example by hand, it does nothing: it would write to the
        // user's folder with them.
        if (Elevation.IsUacElevated)
        {
            return;
        }

        // As when the host starts: the check runs only while the setting is on.
        var paths = AppDataPaths.ForCurrentUser();
        AppSettings settings;
        try
        {
            settings = new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!settings.Updates.CheckAutomatically)
        {
            return;
        }

        using var detailedLogging = new DetailedLogging();
        detailedLogging.TurnOnUntil(settings.Diagnostics.DetailedLoggingUntil);
        using var services = new ServiceCollection()
            .AddLogging(builder => builder.AddFileLog(paths.LogsFolder, "host", detailedLogging: detailedLogging))
            .AddSingleton(paths)
            .AddSingleton(TimeProvider.System)
            .AddSingleton<UpdateCheckStore>()
            .AddSingleton<GitHubReleaseSource>()
            .AddSingleton<UpdateChecker>()
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UpdateCheckCommand));
        try
        {
            services.GetRequiredService<UpdateChecker>().CheckAsync(UpdateNotifier.CurrentVersion, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update check for the host running as administrator failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
