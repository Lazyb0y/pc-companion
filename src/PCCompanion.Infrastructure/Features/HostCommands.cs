using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Infrastructure.Features;

/// <summary>
/// Carries out the host's commands for features on the host's UI thread, from whichever thread
/// asks. The host attaches its UI thread and its own Settings and Exit once its window exists; a
/// command asked for before that is logged and dropped.
/// </summary>
public sealed partial class HostCommands(ILogger<HostCommands> logger) : IHostCommands
{
    private volatile Connection? _connection;

    /// <param name="runOnUiThread">Queues an action on the host's UI thread, from any thread.</param>
    /// <param name="openSettings">The host's Settings, which runs on its UI thread.</param>
    /// <param name="exit">The host's Exit, which runs on its UI thread.</param>
    public void Attach(Action<Action> runOnUiThread, Action openSettings, Action exit)
    {
        ArgumentNullException.ThrowIfNull(runOnUiThread);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(exit);

        _connection = new Connection(runOnUiThread, openSettings, exit);
    }

    public void OpenSettings() => Run("Settings", _connection?.OpenSettings);

    public void ExitApp() => Run("Exit", _connection?.Exit);

    private void Run(string command, Action? action)
    {
        if (_connection is not { } connection || action is null)
        {
            LogNotAttached(logger, command);
            return;
        }

        connection.RunOnUiThread(() =>
        {
            // An exception on the UI thread would end the host.
            try
            {
                LogRunning(logger, command);
                action();
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception, command);
            }
        });
    }

    private sealed record Connection(Action<Action> RunOnUiThread, Action OpenSettings, Action Exit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A feature asked for {Command}.")]
    private static partial void LogRunning(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A feature asked for {Command} before the host was ready, so nothing happened.")]
    private static partial void LogNotAttached(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command}, which a feature asked for, failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string command);
}
