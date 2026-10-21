using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class UpdateCheckWatcherTests : IDisposable
{
    // Long enough for file system events and the watcher's settle delay.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static readonly ReleaseVersion Current = new(0, 1, 0);
    private static readonly ReleaseVersion Newer = new(0, 2, 0);

    private readonly TemporaryFolder _folder = new();
    private readonly RecordingLogger<UpdateCheckWatcher> _logger = new();
    private readonly AppDataPaths _paths;
    private readonly UpdateCheckStore _store;
    private readonly UpdateCheckState _saved;

    public UpdateCheckWatcherTests()
    {
        _paths = new AppDataPaths(Path.Combine(_folder.FullPath, "Data"));
        _store = new UpdateCheckStore(_paths, new RecordingLogger<UpdateCheckStore>());
        _saved = new UpdateCheckState { LastCheck = DateTimeOffset.UnixEpoch, LatestVersion = Newer, NotifiedVersion = Newer };
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ReadsTheResultWhenCreated()
    {
        _store.Save(_saved);

        using var watcher = CreateWatcher();

        Assert.Equal(_saved, watcher.State);
    }

    [Fact]
    public async Task ReadsTheResultAgainAfterASave()
    {
        using var watcher = CreateWatcher();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => changed.TrySetResult();

        _store.Save(_saved);

        await changed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
        Assert.Equal(_saved, watcher.State);
        Assert.Equal(Newer, watcher.UnseenVersion(Current));
    }

    [Fact]
    public void TheBadgeShowsOnlyANewerVersion()
    {
        _store.Save(_saved);

        using var watcher = CreateWatcher();

        Assert.Equal(Newer, watcher.UnseenVersion(Current));
        Assert.Null(watcher.UnseenVersion(Newer));
        Assert.Null(watcher.UnseenVersion(new ReleaseVersion(0, 3, 0)));
    }

    [Fact]
    public void ASavedSeenVersionClearsTheBadgeUntilANewerOne()
    {
        _store.Save(_saved with { SeenVersion = Newer });
        using (var watcher = CreateWatcher())
        {
            Assert.Null(watcher.UnseenVersion(Current));
        }

        _store.Save(_saved with { LatestVersion = new ReleaseVersion(0, 3, 0), SeenVersion = Newer });
        using (var watcher = CreateWatcher())
        {
            Assert.Equal(new ReleaseVersion(0, 3, 0), watcher.UnseenVersion(Current));
        }
    }

    [Fact]
    public void MarkSeenClearsTheBadgeAndSavesTheVersion()
    {
        _store.Save(_saved);
        using var watcher = CreateWatcher();
        var seen = 0;
        watcher.Seen += (_, _) => seen++;

        watcher.MarkSeen(Newer, save: true);

        Assert.Equal(1, seen);
        Assert.True(watcher.IsSeen(Newer));
        Assert.Null(watcher.UnseenVersion(Current));
        Assert.Equal(_saved with { SeenVersion = Newer }, _store.Load());
    }

    [Fact]
    public void MarkSeenKeepsWhatWasSavedSinceTheLastRead()
    {
        _store.Save(_saved);
        using var watcher = CreateWatcher();
        var meanwhile = _saved with { LastCheck = DateTimeOffset.UnixEpoch.AddDays(1) };
        _store.Save(meanwhile);

        watcher.MarkSeen(Newer, save: true);

        Assert.Equal(meanwhile with { SeenVersion = Newer }, _store.Load());
    }

    [Fact]
    public void AVersionThatIsntSavedStaysSeenUntilTheAppExits()
    {
        _store.Save(_saved);
        using (var watcher = CreateWatcher())
        {
            watcher.MarkSeen(Newer, save: false);

            Assert.Null(watcher.UnseenVersion(Current));
            Assert.Equal(_saved, _store.Load());
        }

        using var nextRun = CreateWatcher();
        Assert.Equal(Newer, nextRun.UnseenVersion(Current));
    }

    [Fact]
    public void AVersionThatCantBeSavedStaysSeenAndIsLogged()
    {
        _store.Save(_saved);
        using var watcher = CreateWatcher();

        // The store writes this file first.
        using (new FileStream(_paths.UpdateCheckFile + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            watcher.MarkSeen(Newer, save: true);
        }

        Assert.Null(watcher.UnseenVersion(Current));
        Assert.Equal(_saved, _store.Load());
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Can't save PC Companion 0.2.0", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkingTheSameOrAnOlderVersionAgainDoesNothing()
    {
        _store.Save(_saved with { SeenVersion = new ReleaseVersion(0, 1, 5) });
        using var watcher = CreateWatcher();
        var seen = 0;
        watcher.Seen += (_, _) => seen++;

        watcher.MarkSeen(new ReleaseVersion(0, 1, 5), save: true);
        watcher.MarkSeen(new ReleaseVersion(0, 1, 4), save: true);
        watcher.MarkSeen(Newer, save: true);
        watcher.MarkSeen(Newer, save: true);
        watcher.MarkSeen(new ReleaseVersion(0, 1, 8), save: true);

        Assert.Equal(1, seen);
        Assert.Equal(Newer, _store.Load().SeenVersion);
    }

    private UpdateCheckWatcher CreateWatcher() => new(_store, _paths, _logger);
}
