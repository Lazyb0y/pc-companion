using System;
using System.Threading;
using PCCompanion.Infrastructure.Settings;

namespace PCCompanion.Infrastructure.Updates;

/// <summary>
/// The update check's result for the settings app: read when this is created, and again whenever
/// the host or Check now saves it.
/// </summary>
public sealed class UpdateCheckWatcher : IDisposable
{
    private readonly UpdateCheckStore _store;
    private readonly SettingsWatcher _watcher;
    private UpdateCheckState _state;

    public UpdateCheckWatcher(UpdateCheckStore store, AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(paths);

        _store = store;

        // Watched before it's read, so a save in between isn't missed.
        _watcher = new SettingsWatcher(paths.UpdateCheckFile);
        _watcher.Changed += OnFileChanged;
        _state = store.Load();
    }

    /// <summary>Raised on a thread pool thread after the result was read again.</summary>
    public event EventHandler? Changed;

    /// <summary>The result, as last read.</summary>
    public UpdateCheckState State => Volatile.Read(ref _state);

    public void Dispose() => _watcher.Dispose();

    private void OnFileChanged(object? sender, EventArgs e)
    {
        Volatile.Write(ref _state, _store.Load());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
