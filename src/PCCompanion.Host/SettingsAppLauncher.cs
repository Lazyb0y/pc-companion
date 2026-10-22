using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Host;

/// <summary>
/// Starts the settings app, <c>PCCompanion.exe</c>, which is installed next to the host.
/// </summary>
internal sealed partial class SettingsAppLauncher(ILogger<SettingsAppLauncher> logger)
{
    private static readonly string SettingsAppPath = Path.Combine(AppContext.BaseDirectory, SettingsAppProcess.FileName);

    /// <summary>
    /// Starts the settings app, which hands over to the one that's open, if any.
    /// </summary>
    /// <param name="featureId">The feature whose page to show, or null for the page the settings app shows first.</param>
    public void Open(string? featureId = null)
    {
        LetOpenSettingsAppComeToFront();
        IReadOnlyList<string> arguments = featureId is null ? [] : SettingsAppProcess.FeatureArguments(featureId);
        try
        {
            // The settings app never runs as administrator. A feature's ID needs no quotes.
            if (Elevation.IsElevated)
            {
                UnelevatedProcess.Start(SettingsAppPath, arguments.Count == 0 ? null : string.Join(' ', arguments));
                LogStartedWithoutAdminRights(logger);
                return;
            }

            var startInfo = new ProcessStartInfo(SettingsAppPath) { UseShellExecute = false };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            LogStarted(logger, process?.Id);
        }
        catch (Win32Exception exception)
        {
            LogCantStart(logger, exception, SettingsAppPath);
        }
    }

    // Selecting the tray icon lets the host bring a window to the front. A settings app that's
    // already open gets that right too, to bring its window forward when the new launch hands over
    // to it; without it, Windows only flashes the window's taskbar button. The new launch itself
    // can't be given the right: Windows refuses it to a process that hasn't set up its windows yet.
    private void LetOpenSettingsAppComeToFront()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var settingsApp in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SettingsAppPath)))
        {
            using (settingsApp)
            {
                if (settingsApp.SessionId == current.SessionId && !Foreground.AllowProcess((uint)settingsApp.Id))
                {
                    LogCantLetSettingsAppComeToFront(logger, settingsApp.Id);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Can't let the settings app (process {ProcessId}) bring its window to the front.")]
    private static partial void LogCantLetSettingsAppComeToFront(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started the settings app without admin rights.")]
    private static partial void LogStartedWithoutAdminRights(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Started the settings app (process {ProcessId}).")]
    private static partial void LogStarted(ILogger logger, int? processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Can't start the settings app at {Path}.")]
    private static partial void LogCantStart(ILogger logger, Win32Exception exception, string path);
}
