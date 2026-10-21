using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure.Settings;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class SettingsWatcherTests : IDisposable
{
    // Long enough for file system events and the watcher's settle delay.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);

    private readonly TemporaryFolder _folder = new();
    private readonly AppDataPaths _paths;
    private readonly SettingsStore _store;
    private readonly SettingsWatcher _watcher;
    private int _changes;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SettingsWatcherTests()
    {
        _paths = new AppDataPaths(Path.Combine(_folder.FullPath, "Data"));
        _store = new SettingsStore(_paths, new RecordingLogger<SettingsStore>());
        _watcher = new SettingsWatcher(_paths);
        _watcher.Changed += (_, _) =>
        {
            Interlocked.Increment(ref _changes);
            _changed.TrySetResult();
        };
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ReportsASaveOnce()
    {
        _store.Save(new AppSettings());

        await _changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref _changes));
    }

    [Fact]
    public async Task ReportsEverySave()
    {
        _store.Save(new AppSettings());
        await _changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _store.Save(new AppSettings { General = new GeneralSettings { Theme = AppTheme.Dark } });

        await _changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(2, Volatile.Read(ref _changes));
    }

    [Fact]
    public async Task ReportsADeletedFile()
    {
        _store.Save(new AppSettings());
        await _changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        File.Delete(_paths.SettingsFile);

        await _changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IgnoresOtherFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_paths.Root, "other.json"), "{}", TestContext.Current.CancellationToken);

        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref _changes));
    }

    [Fact]
    public async Task WatchesTheUpdateCheckResultWhenAskedTo()
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new SettingsWatcher(_paths.UpdateCheckFile);
        watcher.Changed += (_, _) => changed.TrySetResult();

        new UpdateCheckStore(_paths, new RecordingLogger<UpdateCheckStore>()).Save(new UpdateCheckState());

        await changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref _changes));
    }

    [Fact]
    public void AFileWithoutAFullPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SettingsWatcher("update-check.json"));
    }
}
