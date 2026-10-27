using System;
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
public sealed partial class FolderRulesFeature(IFolderRulesSystem system, DataFolder dataFolder, TimeProvider time, ILogger<FolderRulesFeature> logger) : IFeature
{
    // How long the host waits as it exits for the thread to finish the file it's handling.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly HistoryStore _store = new(Path.Join(dataFolder.Path, HistoryStore.FileName));
    private readonly SpecialFolders _places = SpecialFolders.ForCurrentUser(dataFolder.Path);
    private FolderRulesThread? _thread;
    private FolderRulesThread? _stopped;
    private bool _failed;
    private bool _paused;

    public FolderRulesFeature(DataFolder dataFolder, TimeProvider time, ILogger<FolderRulesFeature> logger)
        : this(new WindowsFolderRulesSystem(), dataFolder, time, logger)
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
            _thread = new FolderRulesThread(read, system, _store, _places, time, logger, _stopped);
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
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Folder Rules settings can't be read, so Folder Rules is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "PC Companion runs as administrator, so Folder Rules is paused: it never touches files with administrator rights.")]
    private static partial void LogPausedAsAdministrator(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules didn't stop within {Seconds} s, for example while it copied a large file.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);
}
