using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Moves, copies, renames and sorts files as they arrive in the folders the user chose. See
/// docs/features/files/folder-rules.md.
/// </summary>
public sealed partial class FolderRulesFeature(IFolderRulesSystem system, DataFolder dataFolder, string pipeName, TimeProvider time, ILogger<FolderRulesFeature> logger) : IFeature
{
    // How long the host waits as it exits for the thread to finish the file it's handling.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly HistoryStore _store = new(Path.Join(dataFolder.Path, HistoryStore.FileName));
    private readonly SpecialFolders _places = SpecialFolders.ForCurrentUser(dataFolder.Path);
    private FolderRulesThread? _thread;
    private FolderRulesThread? _stopped;
    private Drives? _drives;
    private bool _failed;
    private bool _paused;

    public FolderRulesFeature(DataFolder dataFolder, TimeProvider time, ILogger<FolderRulesFeature> logger)
        : this(new WindowsFolderRulesSystem(), dataFolder, FolderRulesPipe.Name, time, logger)
    {
    }

    public static FeatureInfo Definition { get; } = new("folderRules", FilesModule.Id, "FolderRulesName", "FolderRulesDescription", "");

    /// <summary>
    /// Whether this PC can run Folder Rules, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public void Apply(JsonElement? settings)
    {
        // A failure on the feature's thread keeps the feature off, like a failing Apply.
        if (_thread is { HasFailed: true })
        {
            _failed = true;
            Stop();
        }

        if (_failed)
        {
            return;
        }

        FolderRulesSettings read;
        try
        {
            read = FolderRulesSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop(read);
            return;
        }

        // No setting may make PC Companion touch files as administrator; the folders keep when
        // the rules last looked at them, so files that arrive meanwhile are handled afterwards.
        if (system.IsUacElevated)
        {
            if (!_paused)
            {
                LogPausedAsAdministrator(logger);
                _paused = true;
            }

            Stop();
            return;
        }

        if (_thread is null)
        {
            _drives = new Drives(system.WatchDevices());
            _thread = new FolderRulesThread(read, system, _store, _places, time, _drives, pipeName, logger, _stopped);
            _drives.Follow(_thread);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    // Doesn't wait: a new thread waits for this one before it reads the history. Settings that
    // turn the feature off reach the thread first, so that it forgets when it last looked at the
    // folders, and turning it on again doesn't act on the files that arrived meanwhile.
    private void Stop(FolderRulesSettings? turnedOff = null)
    {
        if (_thread is null)
        {
            return;
        }

        if (turnedOff is not null)
        {
            _thread.Update(turnedOff);
        }

        _thread.CancelWork();
        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
        _drives?.Dispose();
        _drives = null;
    }

    // The host's UI thread's part: Windows' messages about drives and power, and the folders on
    // removable drives, whose watches it ends at once when Windows asks to eject the drive, while
    // the feature's thread may be busy with a file.
    private sealed class Drives(IDeviceWatch devices) : IDriveTracker, IDisposable
    {
        private readonly Dictionary<string, IDisposable> _folders = new(StringComparer.OrdinalIgnoreCase);
        private FolderRulesThread? _thread;

        /// <summary>Tells <paramref name="thread"/> about drives that arrive, ejected drives and waking from sleep.</summary>
        public void Follow(FolderRulesThread thread)
        {
            _thread = thread;
            devices.DriveArrived += (_, _) => thread.LookAgain(everyFolder: false);
            devices.Resumed += (_, _) => thread.LookAgain(everyFolder: true);
        }

        // On the feature's thread.
        public void Watching(string folder, IDisposable watch) => devices.BeginInvoke(() => Register(folder, watch));

        // On the feature's thread.
        public void Stopped(string folder) => devices.BeginInvoke(() => Unregister(folder));

        public void Dispose()
        {
            foreach (var registration in _folders.Values)
            {
                registration.Dispose();
            }

            _folders.Clear();
            devices.Dispose();
        }

        private void Register(string folder, IDisposable watch)
        {
            Unregister(folder);
            try
            {
                _folders[folder] = devices.WatchFolder(folder, () => Ejecting(folder, watch));
            }
            catch (Win32Exception)
            {
                // The drive went meanwhile, which ends the folder's watch too.
            }
        }

        private void Unregister(string folder)
        {
            if (_folders.Remove(folder, out var registration))
            {
                registration.Dispose();
            }
        }

        private void Ejecting(string folder, IDisposable watch)
        {
            watch.Dispose();
            _folders.Remove(folder);
            _thread?.CancelWorkOn(Path.GetPathRoot(folder)!);
            _thread?.DriveLeaving(folder);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Folder Rules settings can't be read, so Folder Rules is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "PC Companion runs as administrator, so Folder Rules is paused: it never touches files with administrator rights.")]
    private static partial void LogPausedAsAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules didn't stop within {Seconds} s, for example while it copied a large file.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
