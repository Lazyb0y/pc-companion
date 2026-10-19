using System;
using System.IO;
using System.Threading;

namespace PCCompanion.Infrastructure.Settings;

/// <summary>
/// Reports changes to <c>settings.json</c>, for processes that read the settings but don't save
/// them, or to another file that another process saves the same way, such as <c>update-check.json</c>.
/// </summary>
public sealed class SettingsWatcher : IDisposable
{
    // One save causes several file system events; they are reported once this long after the last one.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);

    private readonly string _fileName;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    public SettingsWatcher(AppDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).SettingsFile)
    {
    }

    /// <param name="file">The full path of the file to watch. Its folder is created if it doesn't exist.</param>
    public SettingsWatcher(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        var folder = Path.GetDirectoryName(file);
        if (!Path.IsPathFullyQualified(file) || string.IsNullOrEmpty(folder))
        {
            throw new ArgumentException("The file must be a full path.", nameof(file));
        }

        _fileName = Path.GetFileName(file);
        _timer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));

        Directory.CreateDirectory(folder);
        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;

        // Events were lost, so the file may have changed.
        _watcher.Error += (_, _) => ReportChange();
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Raised on a thread pool thread after the file is saved, replaced or deleted.
    /// </summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsWatchedFile(e.Name) || (e is RenamedEventArgs renamed && IsWatchedFile(renamed.OldName)))
        {
            ReportChange();
        }
    }

    private bool IsWatchedFile(string? name) => string.Equals(name, _fileName, StringComparison.OrdinalIgnoreCase);

    private void ReportChange() => _timer.Change(SettleDelay, Timeout.InfiniteTimeSpan);
}
