using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// Imports from, backs up to and opens folders of the drives the user chose when they're connected.
/// See docs/features/files/drive-actions.md.
/// </summary>
/// <param name="pipeName">The pipe through which the settings app asks for the drives, the history, Run now and Undo; <see cref="DriveActionsPipe.Name"/>.</param>
public sealed partial class DriveActionsFeature(
    IDriveActionsSystem system,
    IHostNotifications notifications,
    IHostCommands commands,
    DataFolder dataFolder,
    string pipeName,
    TimeProvider time,
    ILogger<DriveActionsFeature> logger) : IFeature, IHostMenuItems
{
    // The PC Companion icon's menu's items: Stop, then Run actions for each connected drive, from here on.
    private const int StopItem = 1;
    private const int FirstRunItem = 100;

    // How long a new volume's letter has to stay before the drive is looked at: Windows can give a
    // new volume a letter and take it back within milliseconds, as when a tool gives it its own.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    // How long the host's UI thread waits, when Windows asks to eject a drive during a copy, for
    // the copy to let go of its file, so that it doesn't keep Windows from ejecting the drive.
    private static readonly TimeSpan EjectWait = TimeSpan.FromMilliseconds(200);

    // How long the host waits as it exits for the thread to finish the file it's copying.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly IHostNotifications _notifications = notifications;
    private readonly IHostCommands _commands = commands;
    private readonly ILogger _logger = logger;
    private readonly DriveRecordsStore _records = new(Path.Join(dataFolder.Path, DriveRecordsStore.FolderName));
    private readonly DriveActionsHistoryStore _history = new(Path.Join(dataFolder.Path, DriveActionsHistoryStore.FileName));
    private readonly SpecialFolders _places = SpecialFolders.ForCurrentUser(dataFolder.Path);
    private readonly Dictionary<char, IDisposable> _watches = [];
    private volatile DriveActionsThread? _thread;
    private DriveActionsThread? _stopped;
    private volatile IDriveActionsDevices? _devices;
    private volatile DriveActionsState _state = DriveActionsState.Empty;
    private bool _failed;
    private bool _paused;

    public DriveActionsFeature(IHostNotifications notifications, IHostCommands commands, DataFolder dataFolder, TimeProvider time, ILogger<DriveActionsFeature> logger)
        : this(new WindowsDriveActionsSystem(), notifications, commands, dataFolder, DriveActionsPipe.Name, time, logger)
    {
    }

    public static FeatureInfo Definition { get; } = new("driveActions", FilesModule.Id, "DriveActionsName", "DriveActionsDescription", "");

    /// <summary>Whether this PC can run Drive Actions, for the settings app, which doesn't create the feature.</summary>
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

        DriveActionsSettings read;
        try
        {
            read = DriveActionsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        // No setting may make PC Companion touch files as administrator.
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
            _devices = system.WatchDevices();
            _devices.VolumesArrived += (_, e) => Task.Delay(SettleTime).ContinueWith(
                _ => Send(runner =>
                {
                    foreach (var letter in e.Drives)
                    {
                        runner.Arrived(letter, connectedBefore: false);
                    }
                }),
                TaskScheduler.Default);
            _devices.VolumesRemoved += (_, e) => Send(runner =>
            {
                foreach (var letter in e.Drives)
                {
                    runner.Removed(letter);
                }
            });
            _devices.SessionLocked += (_, _) => Send(runner => runner.SessionLocked());
            _devices.SessionUnlocked += (_, _) => Send(runner => runner.SessionUnlocked());
            _thread = new DriveActionsThread(read, system, thread => new Host(this, thread), _records, _history, _places, time, pipeName, logger, _stopped);
            _stopped = null;
        }
        else
        {
            _thread.Update(read);
        }
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems()
    {
        var state = _state;
        if (_thread is null || (state.Connected.Count == 0 && state.Running is null))
        {
            return [];
        }

        var items = new List<HostMenuItem>();
        if (state.Running is { } running)
        {
            items.Add(new HostMenuItem(0, Format(Strings.DriveActionsMenuRunning, running.Name, running.File, running.Files)) { IsEnabled = false });
            items.Add(new HostMenuItem(StopItem, Strings.DriveActionsMenuStop));
            items.Add(HostMenuItem.Separator);
        }

        for (var index = 0; index < state.Connected.Count; index++)
        {
            var drive = state.Connected[index];
            items.Add(new HostMenuItem(FirstRunItem + index, Format(Strings.DriveActionsMenuRun, drive.Name)) { IsEnabled = state.Running?.Letter != drive.Letter });
        }

        return [HostMenuItem.Submenu(Strings.DriveActionsName, items)];
    }

    public void ChooseMenuItem(int id)
    {
        var state = _state;
        if (id == StopItem)
        {
            Send(runner => runner.StopRun());
        }
        else if (id - FirstRunItem is var index && index >= 0 && index < state.Connected.Count)
        {
            var letter = state.Connected[index].Letter;
            Send(runner =>
            {
                if (runner.KeyOf(letter) is { } key)
                {
                    runner.RunRequested(letter, key);
                }
            });
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

    private void Send(Action<DriveActionsRunner> action) => _thread?.Send(action);

    // Doesn't wait: a new thread waits for this one before it reads the history.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        foreach (var letter in _watches.Keys)
        {
            _thread.CancelRunOn(letter);
        }

        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
        foreach (var watch in _watches.Values)
        {
            watch.Dispose();
        }

        _watches.Clear();
        _devices?.Dispose();
        _devices = null;
        _state = DriveActionsState.Empty;
    }

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.CurrentCulture, format, values);

    // What the runner, on the feature's thread, asks of the host's UI thread.
    private sealed class Host(DriveActionsFeature feature, DriveActionsThread thread) : IDriveActionsHost
    {
        public void Notify(string title, string text, Action? selected) =>
            feature.OnUiThread(() =>
            {
                Action? onSelected = selected is null ? null : () => thread.Send(_ => selected());
                feature._notifications.Show(title, text, onSelected);
            });

        public void Watch(char letter) => feature.OnUiThread(() => feature.StartWatch(letter));

        public void Unwatch(char letter) => feature.OnUiThread(() => feature.StopWatch(letter));

        // Like a feature's program, never with admin rights; on the thread pool, since the host waits for Windows.
        public void OpenFolder(string path)
        {
            var explorer = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Task.Run(() =>
            {
                if (!feature._commands.StartProgram(explorer, [path], out var error))
                {
                    LogNotOpened(feature._logger, error);
                }
            });
        }

        public void OpenPage() => feature._commands.OpenFeatureSettings(Definition.Id);

        public void Publish(DriveActionsState state) => feature._state = state;

        public void Later(Action work) => thread.Later(work);
    }

    private void OnUiThread(Action action)
    {
        if (_devices is { } devices && !devices.BeginInvoke(action))
        {
            LogUiThreadGone(logger);
        }
    }

    private void StartWatch(char letter)
    {
        if (_devices is null || _thread is not { } thread || _watches.ContainsKey(letter))
        {
            return;
        }

        try
        {
            _watches[letter] = _devices.WatchVolume(
                letter,
                () =>
                {
                    // At once, on this thread, which Windows waits for: the copy lets go of the
                    // drive, so Windows can eject it.
                    thread.CancelRunOn(letter)?.Wait(EjectWait);
                    _watches.Remove(letter);
                    thread.Send(runner => runner.Ejecting(letter));
                },
                change =>
                {
                    if (change is Platform.Windows.VolumeChange.EjectFailed or Platform.Windows.VolumeChange.Removed)
                    {
                        _watches.Remove(letter);
                    }

                    thread.Send(runner => runner.VolumeChanged(letter, change));
                });
        }
        catch (Win32Exception exception)
        {
            // The drive went meanwhile, which its removal reports too.
            LogNotWatched(logger, letter, exception.NativeErrorCode);
        }
    }

    private void StopWatch(char letter)
    {
        if (_watches.Remove(letter, out var watch))
        {
            watch.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Drive Actions settings can't be read, so Drive Actions is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "PC Companion runs as administrator, so Drive Actions is paused: it never touches files with administrator rights.")]
    private static partial void LogPausedAsAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drive Actions didn't stop within {Seconds} s, for example while it copied a large file.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open the folder of an action (error {ErrorCode}).")]
    private static partial void LogNotOpened(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The drive {Letter} can't be watched (error {ErrorCode}).")]
    private static partial void LogNotWatched(ILogger logger, char letter, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drive Actions asked for the host's UI thread after its window was gone.")]
    private static partial void LogUiThreadGone(ILogger logger);
}
