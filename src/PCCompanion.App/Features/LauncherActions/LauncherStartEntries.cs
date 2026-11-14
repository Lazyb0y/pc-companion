using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.LauncherActions;

namespace PCCompanion.App.Features.LauncherActions;

/// <summary>
/// Keeps Launcher Actions' entries in Start as the settings say: the settings app, which never runs
/// as administrator, writes them on the thread pool when it starts and after each save, and only
/// what changed. See "Start entries" in docs/features/input/launcher-actions.md.
/// </summary>
public sealed partial class LauncherStartEntries(AppSettings settings, SettingsStore store, ILogger<LauncherStartEntries> logger)
{
    private readonly object _gate = new();
    private Task _writing = Task.CompletedTask;

    /// <summary>Writes the entries now and after each save. Call it once, on the UI thread.</summary>
    public void Start()
    {
        store.Saved += (_, _) => Update();
        Update();
    }

    // On the UI thread, which changes the settings: the sections are copied there, and the entries
    // written on the thread pool, one update after the other.
    private void Update()
    {
        var sections = new Dictionary<string, JsonElement>(settings.Features);
        lock (_gate)
        {
            _writing = _writing.ContinueWith(_ => Write(sections), TaskScheduler.Default);
        }
    }

    private void Write(Dictionary<string, JsonElement> sections)
    {
        try
        {
            var entries = Entries(featureId => sections.TryGetValue(featureId, out var section) ? section : null);
            var written = new LauncherStartFolder().Update(LauncherActionList.FileNameOf(Strings.LauncherActionsStartFolder), entries, Program);
            if (written > 0 || entries.Count == 0)
            {
                LogUpdated(logger, entries.Count, written);
            }
        }
        catch (Exception exception)
        {
            // The next update tries again. Paths aren't logged: entries are named for profiles and apps.
            LogNotWritten(logger, exception.GetType().Name, exception.HResult);
        }
    }

    /// <summary>
    /// The entries the settings ask for: none while Launcher Actions or Show actions in Start is
    /// off, or its section can't be read, and otherwise those of the actions available now, except
    /// the features left out of Start.
    /// </summary>
    private static IReadOnlyList<StartEntry> Entries(Func<string, JsonElement?> section)
    {
        LauncherActionsSettings launcher;
        try
        {
            launcher = LauncherActionsSettings.Read(section(LauncherActionsFeature.Definition.Id));
        }
        catch (JsonException)
        {
            return [];
        }

        return launcher.Enabled && launcher.ShowInStart
            ? LauncherActionList.Entries(LauncherCatalog.Read(section).Where(action => launcher.IsInStart(action.Action.FeatureId)), Strings.LauncherActionsEntryName)
            : [];
    }

    // The settings app itself, which runs the command.
    private static string Program => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, SettingsAppProcess.FileName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Launcher Actions' entries in Start were updated: {Entries} entries, {Written} written.")]
    private static partial void LogUpdated(ILogger logger, int entries, int written);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Launcher Actions' entries in Start couldn't be written: {Exception} 0x{HResult:X8}.")]
    private static partial void LogNotWritten(ILogger logger, string exception, int hResult);
}
