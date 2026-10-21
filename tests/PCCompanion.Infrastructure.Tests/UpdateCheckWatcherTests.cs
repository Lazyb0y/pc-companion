using System;
using System.IO;
using System.Threading.Tasks;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class UpdateCheckWatcherTests : IDisposable
{
    // Long enough for file system events and the watcher's settle delay.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly TemporaryFolder _folder = new();
    private readonly AppDataPaths _paths;
    private readonly UpdateCheckStore _store;

    public UpdateCheckWatcherTests()
    {
        _paths = new AppDataPaths(Path.Combine(_folder.FullPath, "Data"));
        _store = new UpdateCheckStore(_paths, new RecordingLogger<UpdateCheckStore>());
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ReadsTheResultWhenCreated()
    {
        var saved = new UpdateCheckState { LastCheck = DateTimeOffset.UnixEpoch, LatestVersion = new ReleaseVersion(0, 2, 0) };
        _store.Save(saved);

        using var watcher = CreateWatcher();

        Assert.Equal(saved, watcher.State);
    }

    [Fact]
    public async Task ReadsTheResultAgainAfterASave()
    {
        using var watcher = CreateWatcher();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => changed.TrySetResult();
        var saved = new UpdateCheckState { LastCheck = DateTimeOffset.UnixEpoch, LatestVersion = new ReleaseVersion(0, 2, 0) };

        _store.Save(saved);

        await changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(saved, watcher.State);
    }

    private UpdateCheckWatcher CreateWatcher() => new(_store, _paths);
}
