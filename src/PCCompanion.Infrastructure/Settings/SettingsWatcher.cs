using System;
using System.IO;
using System.Threading;

namespace PCCompanion.Infrastructure.Settings;

/// <summary>
/// Reports changes to <c>settings.json</c>, for processes that read the settings but don't save them.
/// </summary>
public sealed class SettingsWatcher : IDisposable
{
    // One save causes several file system events; they are reported once this long after the last one.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(200);

    private readonly string _fileName;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    public SettingsWatcher(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _fileName = Path.GetFileName(paths.SettingsFile);
        _timer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));

        Directory.CreateDirectory(paths.Root);
        _watcher = new FileSystemWatcher(paths.Root) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;

        // Events were lost, so the file may have changed.
        _watcher.Error += (_, _) => ReportChange();
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Raised on a thread pool thread after the settings file is saved, replaced or deleted.
    /// </summary>
    public event EventHandler? Changed;

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsSettingsFile(e.Name) || (e is RenamedEventArgs renamed && IsSettingsFile(renamed.OldName)))
        {
            ReportChange();
        }
    }

    private bool IsSettingsFile(string? name) => string.Equals(name, _fileName, StringComparison.OrdinalIgnoreCase);

    private void ReportChange() => _timer.Change(SettleDelay, Timeout.InfiniteTimeSpan);
}
