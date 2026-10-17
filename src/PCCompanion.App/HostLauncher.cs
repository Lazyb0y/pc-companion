using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;

namespace PCCompanion.App;

/// <summary>
/// Starts the background host, installed next to the settings app, when it isn't running.
/// </summary>
internal sealed partial class HostLauncher(ILogger<HostLauncher> logger)
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, HostProcess.FileName);

    public void StartIfNotRunning()
    {
        if (Mutex.TryOpenExisting(HostProcess.InstanceLockName, out var instanceLock))
        {
            instanceLock.Dispose();
            LogAlreadyRunning(logger);
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(HostPath) { UseShellExecute = false });
            LogStarted(logger);
        }
        catch (Win32Exception exception)
        {
            LogCantStart(logger, exception, HostPath);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The background host is already running.")]
    private static partial void LogAlreadyRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the background host.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't start the background host at {Path}.")]
    private static partial void LogCantStart(ILogger logger, Win32Exception exception, string path);
}
