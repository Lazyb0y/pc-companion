using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Settings;

namespace PCCompanion.Infrastructure.Updates;

/// <summary>
/// The update check's result for the settings app: read when this is created, and again whenever
/// the host or Check now saves it. It also keeps the version the General page showed, which
/// clears the badge on its item. See docs/features/general/update-check.md.
/// </summary>
public sealed partial class UpdateCheckWatcher : IDisposable
{
    private readonly UpdateCheckStore _store;
    private readonly ILogger<UpdateCheckWatcher> _logger;
    private readonly SettingsWatcher _watcher;
    private readonly Lock _lock = new();
    private UpdateCheckState _state;

    // The version the General page showed in this run, also when it isn't saved.
    private ReleaseVersion? _seen;

    public UpdateCheckWatcher(UpdateCheckStore store, AppDataPaths paths, ILogger<UpdateCheckWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _logger = logger;

        // Watched before it's read, so a save in between isn't missed.
        _watcher = new SettingsWatcher(paths.UpdateCheckFile);
        _watcher.Changed += OnFileChanged;
        _state = store.Load();
    }

    /// <summary>Raised on a thread pool thread after the result was read again.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised on the thread that calls <see cref="MarkSeen"/>, when it keeps a newer version.</summary>
    public event EventHandler? Seen;

    /// <summary>The result, as last read.</summary>
    public UpdateCheckState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// The version for the badge on the General item: one newer than <paramref name="current"/>
    /// that the General page hasn't shown yet, or <see langword="null"/>.
    /// </summary>
    public ReleaseVersion? UnseenVersion(ReleaseVersion current)
    {
        lock (_lock)
        {
            return _state.NewerThan(current) is { } version && !IsSeenLocked(version) ? version : null;
        }
    }

    /// <summary>Whether the General page showed <paramref name="version"/> or a newer one.</summary>
    public bool IsSeen(ReleaseVersion version)
    {
        lock (_lock)
        {
            return IsSeenLocked(version);
        }
    }

    /// <summary>
    /// Keeps <paramref name="version"/> as the one the General page showed, which clears the
    /// badge, and saves it when <paramref name="save"/> is true. It stays seen until this app
    /// exits, also when it isn't or can't be saved. It reads and saves the file, so call it off
    /// the window's thread.
    /// </summary>
    public void MarkSeen(ReleaseVersion version, bool save)
    {
        lock (_lock)
        {
            if (IsSeenLocked(version))
            {
                return;
            }

            _seen = version;
        }

        LogSeen(_logger, version);
        Seen?.Invoke(this, EventArgs.Empty);
        if (!save)
        {
            return;
        }

        // Read again just before saving, so what the host saved in the meantime is kept.
        try
        {
            _store.Save(_store.Load() with { SeenVersion = version });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantSave(_logger, exception, version);
        }
    }

    public void Dispose() => _watcher.Dispose();

    private bool IsSeenLocked(ReleaseVersion version) => _state.SeenVersion >= version || _seen >= version;

    private void OnFileChanged(object? sender, EventArgs e)
    {
        var state = _store.Load();
        lock (_lock)
        {
            _state = state;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The General page showed PC Companion {Version}, so the badge on its item goes away.")]
    private static partial void LogSeen(ILogger logger, ReleaseVersion version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't save PC Companion {Version} as the version the General page showed; the badge on its item comes back the next time the settings app starts.")]
    private static partial void LogCantSave(ILogger logger, Exception exception, ReleaseVersion version);
}
