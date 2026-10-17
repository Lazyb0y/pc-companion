using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Starts the settings app, <c>PCCompanion.exe</c>, which is installed next to the host.
/// </summary>
internal sealed partial class SettingsAppLauncher(ILogger<SettingsAppLauncher> logger)
{
    private static readonly string SettingsAppPath = Path.Combine(AppContext.BaseDirectory, "PCCompanion.exe");

    public void Open()
    {
        try
        {
            // The settings app never runs as administrator.
            if (Elevation.IsElevated)
            {
                UnelevatedProcess.Start(SettingsAppPath);
                LogStartedWithoutAdminRights(logger);
                return;
            }

            using var process = Process.Start(new ProcessStartInfo(SettingsAppPath) { UseShellExecute = false });
            LogStarted(logger, process?.Id);
        }
        catch (Win32Exception exception)
        {
            LogCantStart(logger, exception, SettingsAppPath);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started the settings app without admin rights.")]
    private static partial void LogStartedWithoutAdminRights(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started the settings app (process {ProcessId}).")]
    private static partial void LogStarted(ILogger logger, int? processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't start the settings app at {Path}.")]
    private static partial void LogCantStart(ILogger logger, Win32Exception exception, string path);
}
