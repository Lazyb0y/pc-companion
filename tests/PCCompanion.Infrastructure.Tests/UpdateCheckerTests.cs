using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Updates;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly ReleaseVersion Current = new(0, 1, 0);

    private readonly TemporaryFolder _folder = new();
    private readonly FakeGitHub _gitHub = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<UpdateChecker> _logger = new();
    private readonly AppDataPaths _paths;
    private readonly UpdateCheckStore _store;

    public UpdateCheckerTests()
    {
        _paths = new AppDataPaths(Path.Combine(_folder.FullPath, "Data"));
        _store = new UpdateCheckStore(_paths, new RecordingLogger<UpdateCheckStore>());
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ANewerVersionIsSavedAndShownOnce()
    {
        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered, new ReleaseVersion(0, 2, 0)), outcome);
        Assert.Equal(
            new UpdateCheckState { LastCheck = _time.Now, LatestVersion = new ReleaseVersion(0, 2, 0), NotifiedVersion = new ReleaseVersion(0, 2, 0) },
            _store.Load());
        Assert.Contains((LogLevel.Information, "PC Companion 0.2.0 is available; this is 0.1.0."), _logger.Entries);

        _time.Advance(UpdateChecker.Interval);
        var next = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), next);
        Assert.Equal(_time.Now, _store.Load().LastCheck);
    }

    [Fact]
    public async Task AnEvenNewerVersionIsShownToo()
    {
        await CheckAsync();
        _time.Advance(UpdateChecker.Interval);
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Release("v0.3.0"));

        var outcome = await CheckAsync();

        Assert.Equal(new ReleaseVersion(0, 3, 0), outcome.NewVersion);
        Assert.Equal(new ReleaseVersion(0, 3, 0), _store.Load().NotifiedVersion);
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    public async Task TheSameOrAnOlderVersionIsNoUpdate(string tag)
    {
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Release(tag));

        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), outcome);
        Assert.Null(_store.Load().NotifiedVersion);
        Assert.Contains((LogLevel.Debug, "PC Companion 0.1.0 is up to date."), _logger.Entries);
    }

    [Fact]
    public async Task NothingIsAskedLessThanADayAfterTheLastAnswer()
    {
        _store.Save(new UpdateCheckState { LastCheck = _time.Now - UpdateChecker.Interval + TimeSpan.FromMinutes(1) });

        var outcome = await CheckAsync();

        Assert.Equal(UpdateCheckResult.NotDue, outcome.Result);
        Assert.Empty(_gitHub.Requests);
    }

    [Fact]
    public async Task ACheckIsDueADayAfterTheLastAnswer()
    {
        _store.Save(new UpdateCheckState { LastCheck = _time.Now - UpdateChecker.Interval });

        await CheckAsync();

        Assert.Single(_gitHub.Requests);
    }

    [Fact]
    public async Task ALastCheckInTheFutureCountsAsDue()
    {
        _store.Save(new UpdateCheckState { LastCheck = _time.Now + TimeSpan.FromDays(300) });

        await CheckAsync();

        Assert.Single(_gitHub.Requests);
        Assert.Equal(_time.Now, _store.Load().LastCheck);
    }

    [Fact]
    public async Task NoReleaseClearsTheLatestVersion()
    {
        _store.Save(new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 2, 0), NotifiedVersion = new ReleaseVersion(0, 2, 0) });
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Status(HttpStatusCode.NotFound));

        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), outcome);
        Assert.Equal(new UpdateCheckState { LastCheck = _time.Now, NotifiedVersion = new ReleaseVersion(0, 2, 0) }, _store.Load());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnErrorCountsAsTheDaysCheckAndKeepsTheLatestVersion(HttpStatusCode status)
    {
        _store.Save(new UpdateCheckState { LatestVersion = new ReleaseVersion(0, 2, 0), NotifiedVersion = new ReleaseVersion(0, 2, 0) });
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Status(status));

        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), outcome);
        Assert.Equal(new UpdateCheckState { LastCheck = _time.Now, LatestVersion = new ReleaseVersion(0, 2, 0), NotifiedVersion = new ReleaseVersion(0, 2, 0) }, _store.Load());
        Assert.Contains((LogLevel.Warning, $"GitHub answered the update check with status {(int)status}."), _logger.Entries);
    }

    [Fact]
    public async Task AnAnswerThatCantBeUsedCountsAsTheDaysCheckAndIsntLogged()
    {
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Release("v0.2.0-<script>secret</script>"));

        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), outcome);
        Assert.Equal(new UpdateCheckState { LastCheck = _time.Now }, _store.Load());
        Assert.Contains((LogLevel.Warning, "GitHub's answer to the update check (status 200) can't be used: its tag isn't a version such as v1.2.0."), _logger.Entries);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoAnswerChangesNothing()
    {
        _gitHub.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known."));

        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.NoAnswer), outcome);
        Assert.False(File.Exists(_paths.UpdateCheckFile));
        Assert.Contains((LogLevel.Debug, "The update check got no answer from GitHub (NameResolutionError)."), _logger.Entries);
    }

    [Fact]
    public async Task AResultThatCantBeSavedStillCounts()
    {
        // A file where the data folder should be.
        Directory.CreateDirectory(_folder.FullPath);
        File.WriteAllText(_paths.Root, string.Empty);

        var outcome = await CheckAsync();

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered, new ReleaseVersion(0, 2, 0)), outcome);
        Assert.Contains((LogLevel.Warning, "Can't save the update check's result."), _logger.Entries);
    }

    [Fact]
    public async Task NothingIsLoggedAsAnError()
    {
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Json("not json"));
        await CheckAsync();
        _time.Advance(UpdateChecker.Interval);
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Status(HttpStatusCode.TooManyRequests));
        await CheckAsync();

        Assert.DoesNotContain(_logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public void DueIsDecidedFromTheLastAnswer()
    {
        var now = _time.Now;

        Assert.True(UpdateChecker.IsDue(new UpdateCheckState(), now));
        Assert.False(UpdateChecker.IsDue(new UpdateCheckState { LastCheck = now }, now));
        Assert.False(UpdateChecker.IsDue(new UpdateCheckState { LastCheck = now - TimeSpan.FromHours(23) }, now));
        Assert.True(UpdateChecker.IsDue(new UpdateCheckState { LastCheck = now - TimeSpan.FromHours(24) }, now));
        Assert.True(UpdateChecker.IsDue(new UpdateCheckState { LastCheck = now + TimeSpan.FromSeconds(1) }, now));
    }

    [Fact]
    public void ACopyThatSavedANewAnswerAnswered()
    {
        var before = new UpdateCheckState { LastCheck = _time.Now - TimeSpan.FromDays(2), LatestVersion = new ReleaseVersion(0, 1, 0) };
        var after = before with { LastCheck = _time.Now };

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), UpdateChecker.OutcomeOfCopy(before, after, Current));
    }

    [Fact]
    public void ACopyThatSavedANewVersionAsShownHasTheNotificationShown()
    {
        var before = new UpdateCheckState { NotifiedVersion = new ReleaseVersion(0, 2, 0) };
        var after = new UpdateCheckState { LastCheck = _time.Now, LatestVersion = new ReleaseVersion(0, 3, 0), NotifiedVersion = new ReleaseVersion(0, 3, 0) };

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered, new ReleaseVersion(0, 3, 0)), UpdateChecker.OutcomeOfCopy(before, after, Current));
    }

    [Fact]
    public void ACopyThatSavedNoNewAnswerGotNoAnswer()
    {
        var before = new UpdateCheckState { LastCheck = _time.Now - TimeSpan.FromDays(2) };

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.NoAnswer), UpdateChecker.OutcomeOfCopy(before, before, Current));
        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.NoAnswer), UpdateChecker.OutcomeOfCopy(new UpdateCheckState(), new UpdateCheckState(), Current));
    }

    [Fact]
    public void AChangedFileCantShowAVersionThatIsntNewer()
    {
        var after = new UpdateCheckState { LastCheck = _time.Now, NotifiedVersion = new ReleaseVersion(0, 0, 1) };

        Assert.Equal(new UpdateCheckOutcome(UpdateCheckResult.Answered), UpdateChecker.OutcomeOfCopy(new UpdateCheckState(), after, Current));
    }

    private Task<UpdateCheckOutcome> CheckAsync() =>
        new UpdateChecker(_store, new GitHubReleaseSource(_gitHub.CreateHandler, _time), _time, _logger)
            .CheckAsync(Current, TestContext.Current.CancellationToken);
}
