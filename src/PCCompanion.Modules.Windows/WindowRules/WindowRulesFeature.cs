using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.WindowRules;

/// <summary>
/// Rules that act on an app's windows as they open: their display, place and size, how they open,
/// and keeping them on top, translucent, off the taskbar or in a tab group. See
/// docs/features/windows/window-rules.md.
/// </summary>
public sealed partial class WindowRulesFeature : IFeature
{
    // How long the host waits as it exits for the feature's thread, and then for the file.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly ILogger<WindowRulesFeature> _logger;
    private readonly DropDownWindows _dropDown;
    private readonly WindowTabsWindows _tabs;
    private readonly WindowRequests _requests;
    private readonly string? _pipeName;
    private readonly Func<IRulesSystem> _recoverySystem;
    private readonly RememberedPlacesStore _store;
    private readonly bool _isUacElevated;

    // The file's reads, writes and deletes run on the thread pool one after another, in order,
    // asked for from the host's UI thread and the feature's thread.
    private readonly Lock _fileGate = new();
    private Task _fileWork = Task.CompletedTask;

    // Whether the file was deleted since the last write, so it isn't deleted again at each save of the settings.
    private bool _fileDeleted;
    private WindowRulesThread? _thread;
    private WindowRulesThread? _stopped;
    private bool _failed;
    private bool _recovered;

    /// <param name="dataFolder">Where <c>window-rules.json</c> keeps what rules remember.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    /// <param name="requests">Where windows go to Window Tabs, and where Title Bar Actions tells of the wheel's opacity.</param>
    public WindowRulesFeature(TimeProvider time, ILogger<WindowRulesFeature> logger, DataFolder dataFolder, DropDownWindows dropDown, WindowTabsWindows tabs, WindowRequests requests)
        : this(time, logger, dataFolder, Elevation.IsUacElevated, dropDown, tabs, requests, WindowRulesPipe.Name, () => new WindowsRulesSystem(action => action(), action => action(), dropDown, tabs, requests))
    {
    }

    /// <param name="dataFolder">Where <c>window-rules.json</c> keeps what rules remember.</param>
    /// <param name="isUacElevated">Whether the host runs as administrator after a UAC prompt, when the file isn't used.</param>
    /// <param name="dropDown">Drop-down Apps' windows, which rules leave alone.</param>
    /// <param name="tabs">Window Tabs' tabs, which rules leave alone.</param>
    /// <param name="requests">Where windows go to Window Tabs, and where Title Bar Actions tells of the wheel's opacity.</param>
    /// <param name="pipeName">The pipe the settings app asks through for Apply now; none in tests.</param>
    /// <param name="recoverySystem">Where the windows left marked are found, as tests supply it.</param>
    public WindowRulesFeature(
        TimeProvider time,
        ILogger<WindowRulesFeature> logger,
        DataFolder dataFolder,
        bool isUacElevated,
        DropDownWindows dropDown,
        WindowTabsWindows tabs,
        WindowRequests requests,
        string? pipeName,
        Func<IRulesSystem> recoverySystem)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dataFolder);
        ArgumentNullException.ThrowIfNull(dropDown);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(recoverySystem);

        _time = time;
        _logger = logger;
        _dropDown = dropDown;
        _tabs = tabs;
        _requests = requests;
        _pipeName = pipeName;
        _recoverySystem = recoverySystem;
        _store = new RememberedPlacesStore(Path.Join(dataFolder.Path, RememberedPlacesStore.FileName));
        _isUacElevated = isUacElevated;

        // A crash ends the host without stopping the feature, so the windows rules changed are put back first.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public static FeatureInfo Definition { get; } = new("windowRules", WindowsModule.Id, "WindowRulesName", "WindowRulesDescription", "\uE8A1");

    /// <summary>
    /// Whether this PC can run Window Rules, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _thread is not null;

    public void Apply(JsonElement? settings)
    {
        // Windows left changed by a PC Companion that ended without putting them back are put
        // back, on or off; it reads every window's properties without waiting for any program.
        if (!_recovered)
        {
            _recovered = true;
            var recovered = RulesController.Recover(_recoverySystem());
            if (recovered > 0)
            {
                LogRecovered(_logger, recovered);
            }
        }

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

        WindowRulesSettings read;
        try
        {
            read = WindowRulesSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            Stop();
            return;
        }

        // Turning the feature off forgets what rules remember.
        if (!read.Enabled)
        {
            DeleteFile();
            Stop();
            return;
        }

        if (_thread is null)
        {
            var thread = new WindowRulesThread(read, _time, _logger, _dropDown, _tabs, _requests, _stopped, _pipeName, keeps: !_isUacElevated, ReadFile, SaveFile);
            _thread = thread;
            _stopped = null;
            _requests.SetOpacityChanged(thread.ReportWheelOpacity);
        }
        else
        {
            _thread.Update(read);
        }
    }

    // The host disposes features twice as it exits, so the thread and the file are waited for only once.
    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        Stop();
        if (_stopped is { } stopped && !stopped.Join(StopTimeout))
        {
            LogNotStoppedInTime(_logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
        Task fileWork;
        lock (_fileGate)
        {
            fileWork = _fileWork;
            _fileWork = Task.CompletedTask;
        }

        if (!fileWork.Wait(FileTimeout))
        {
            LogFileNotDoneInTime(_logger, FileTimeout.TotalSeconds);
        }
    }

    // Doesn't wait: a new thread waits for this one before it starts.
    private void Stop()
    {
        if (_thread is null)
        {
            return;
        }

        _requests.SetOpacityChanged(null);
        _thread.Stop();
        (_stopped, _thread) = (_thread, null);
    }

    // The feature's thread asks for what rules remember as it starts; it's read and passed back.
    private void ReadFile(WindowRulesThread thread) => OnFileThread(() =>
    {
        var rules = _store.Load(out var readable);
        if (!readable)
        {
            LogUnreadableFile(_logger);
        }

        if (rules.Count > 0)
        {
            thread.LoadRemembered(rules);
        }
    });

    // The feature's thread passes what rules remember after a change and as it stops. It's
    // written only if the thread still keeps it when it's its turn, so a write asked for before
    // the feature was turned off doesn't bring the file back.
    private void SaveFile(WindowRulesThread thread, IReadOnlyList<RememberedRule> rules)
    {
        lock (_fileGate)
        {
            _fileDeleted = false;
        }

        OnFileThread(() =>
        {
            if (!thread.Keeps)
            {
                return;
            }

            try
            {
                _store.Save(rules);
                LogFileSaved(_logger, rules.Count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFileNotSaved(_logger, exception);
            }
        });
    }

    // Forgetting deletes the file, once, unless the host runs as administrator after a UAC
    // prompt, when a program without admin rights could have put a link in its place. Neither the
    // thread nor one still stopping writes it again.
    private void DeleteFile()
    {
        if (_thread is not null)
        {
            _thread.Keeps = false;
        }

        if (_stopped is not null)
        {
            _stopped.Keeps = false;
        }

        lock (_fileGate)
        {
            if (_fileDeleted || _isUacElevated)
            {
                return;
            }

            _fileDeleted = true;
        }

        OnFileThread(() =>
        {
            try
            {
                _store.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFileNotDeleted(_logger, exception);
            }
        });
    }

    private void OnFileThread(Action work)
    {
        lock (_fileGate)
        {
            _fileWork = _fileWork.ContinueWith(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception exception)
                    {
                        LogFileWorkFailed(_logger, exception);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    // Runs on the crashing thread; nothing here may throw.
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _thread?.ReleaseNow();
        }
        catch (Exception exception)
        {
            LogReleaseFailed(_logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules put back {Count} windows left changed.")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Rules couldn't put back the windows it changed as PC Companion crashed.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Window Rules settings can't be read, so Window Rules is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules' remembered places can't be read, so none are used; the file is replaced at the next save.")]
    private static partial void LogUnreadableFile(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Window Rules saved what {Count} rules remember.")]
    private static partial void LogFileSaved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules couldn't save its remembered places.")]
    private static partial void LogFileNotSaved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules couldn't delete its remembered places.")]
    private static partial void LogFileNotDeleted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Window Rules' work on its remembered places failed.")]
    private static partial void LogFileWorkFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Window Rules' remembered places weren't written within {Seconds} s.")]
    private static partial void LogFileNotDoneInTime(ILogger logger, double seconds);
}
