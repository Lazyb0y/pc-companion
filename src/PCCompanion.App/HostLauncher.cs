using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App;

/// <summary>
/// Starts the background host, installed next to the settings app, when it isn't running.
/// </summary>
public sealed partial class HostLauncher(ILogger<HostLauncher> logger)
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, HostProcess.FileName);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    public void StartIfNotRunning()
    {
        if (IsRunning())
        {
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

    /// <summary>
    /// Asks the running host to exit, waits for it, and starts it again, for example after
    /// Always run as administrator changed.
    /// </summary>
    public async Task RestartAsync()
    {
        if (IsRunning())
        {
            MessageWindow.PostToWindow(HostProcess.WindowClassName, MessageWindow.RegisterMessage(HostProcess.ExitMessageName));
            var waited = Stopwatch.StartNew();
            while (IsRunning() && waited.Elapsed < ExitTimeout)
            {
                await Task.Delay(200).ConfigureAwait(true);
            }
        }

        StartIfNotRunning();
    }

    // A host running as administrator holds a lock that this process may not open.
    private static bool IsRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(HostProcess.InstanceLockName, out var instanceLock))
            {
                return false;
            }

            instanceLock.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The background host is already running.")]
    private static partial void LogAlreadyRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the background host.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't start the background host at {Path}.")]
    private static partial void LogCantStart(ILogger logger, Win32Exception exception, string path);
}
