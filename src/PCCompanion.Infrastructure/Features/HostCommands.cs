using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;

namespace PCCompanion.Infrastructure.Features;

/// <summary>
/// Carries out the host's commands for features: Settings and Exit on the host's UI thread, from
/// whichever thread asks, and web pages and programs on the calling thread. The host attaches its UI
/// thread and its own commands once its window exists; a command asked for before that is logged
/// and dropped.
/// </summary>
public sealed partial class HostCommands(ILogger<HostCommands> logger) : IHostCommands
{
    private const string WebPage = "a web page";
    private const string Program = "a program";
    private const int NotReady = 21; // ERROR_NOT_READY

    private volatile Connection? _connection;

    /// <param name="runOnUiThread">Queues an action on the host's UI thread, from any thread.</param>
    /// <param name="openSettings">
    /// The host's Settings, which runs on its UI thread, with the ID of the feature whose page to
    /// show, or null for the page the settings app shows first.
    /// </param>
    /// <param name="exit">The host's Exit, which runs on its UI thread.</param>
    /// <param name="openWebPage">
    /// Opens a web page without admin rights, on the calling thread, and returns false when it can't.
    /// </param>
    /// <param name="startProgram">
    /// Starts a program and its arguments without admin rights, on the calling thread, and returns 0,
    /// or Windows' error code when it can't.
    /// </param>
    public void Attach(Action<Action> runOnUiThread, Action<string?> openSettings, Action exit, Func<Uri, bool> openWebPage, Func<string, IReadOnlyList<string>, int> startProgram)
    {
        ArgumentNullException.ThrowIfNull(runOnUiThread);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(openWebPage);
        ArgumentNullException.ThrowIfNull(startProgram);

        _connection = new Connection(runOnUiThread, openSettings, exit, openWebPage, startProgram);
    }

    public void OpenSettings() => Run("Settings", connection => connection.OpenSettings(null));

    public void OpenFeatureSettings(string featureId)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureId);

        Run("a feature's settings", connection => connection.OpenSettings(featureId));
    }

    public void ExitApp() => Run("Exit", connection => connection.Exit());

    // Not logged with the address, which can hold what a feature's user copied or typed.
    public bool OpenWebPage(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_connection is not { } connection)
        {
            LogNotAttached(logger, WebPage);
            return false;
        }

        LogRunning(logger, WebPage);
        return connection.OpenWebPage(address);
    }

    // Not logged with the program or its arguments, which can hold personal paths or what a
    // feature's user copied.
    public bool StartProgram(string program, IReadOnlyList<string> arguments, out int errorCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);

        if (_connection is not { } connection)
        {
            LogNotAttached(logger, Program);
            errorCode = NotReady;
            return false;
        }

        LogRunning(logger, Program);
        errorCode = connection.StartProgram(program, arguments);
        return errorCode == 0;
    }

    private void Run(string command, Action<Connection> action)
    {
        if (_connection is not { } connection)
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
                action(connection);
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception, command);
            }
        });
    }

    private sealed record Connection(Action<Action> RunOnUiThread, Action<string?> OpenSettings, Action Exit, Func<Uri, bool> OpenWebPage, Func<string, IReadOnlyList<string>, int> StartProgram);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A feature asked for {Command}.")]
    private static partial void LogRunning(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A feature asked for {Command} before the host was ready, so nothing happened.")]
    private static partial void LogNotAttached(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command}, which a feature asked for, failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string command);
}
