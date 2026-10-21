using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class UpdateCheckSchedulerTests : IDisposable
{
    private static readonly UpdateCheckOutcome Answered = new(UpdateCheckResult.Answered);
    private static readonly UpdateCheckOutcome NoAnswer = new(UpdateCheckResult.NoAnswer);

    private readonly TemporaryFolder _folder = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<UpdateCheckScheduler> _logger = new();
    private readonly UpdateCheckStore _store;
    private readonly List<ReleaseVersion> _found = [];
    private readonly UpdateCheckScheduler _scheduler;
    private int _checks;

    public UpdateCheckSchedulerTests()
    {
        _store = new UpdateCheckStore(new AppDataPaths(Path.Combine(_folder.FullPath, "Data")), new RecordingLogger<UpdateCheckStore>());
        _scheduler = new UpdateCheckScheduler(
            cancellationToken =>
            {
                _checks++;
                return Check(cancellationToken);
            },
            _store,
            _time,
            _logger);
        _scheduler.UpdateFound += (_, e) => _found.Add(e.Version);
    }

    // What each check does; by default GitHub answers.
    private Func<CancellationToken, Task<UpdateCheckOutcome>> Check { get; set; } = _ => Task.FromResult(Answered);

    public void Dispose()
    {
        _scheduler.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void TheFirstCheckIsTwoMinutesAfterStart()
    {
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, _checks);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _checks);
    }

    [Fact]
    public void AfterAnAnswerTheNextCheckIsADayLater()
    {
        _scheduler.Apply(checkAutomatically: true);
        _time.Advance(UpdateCheckScheduler.StartDelay);

        _time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(1, _checks);

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, _checks);
    }

    [Fact]
    public void NoAnswerIsTriedAgainEveryHourAndLoggedOnceAsInformation()
    {
        Check = _ => Task.FromResult(NoAnswer);
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(UpdateCheckScheduler.StartDelay + TimeSpan.FromHours(2));

        Assert.Equal(3, _checks);
        Assert.Equal(
            [
                (LogLevel.Information, "Can't reach GitHub to check for updates; trying again in an hour."),
                (LogLevel.Debug, "Still can't reach GitHub to check for updates; trying again in an hour."),
                (LogLevel.Debug, "Still can't reach GitHub to check for updates; trying again in an hour."),
            ],
            _logger.Entries.Where(entry => entry.Message.Contains("GitHub", StringComparison.Ordinal)));
    }

    [Fact]
    public void ARecentAnswerInTheFileMeansNoCheckUntilADayAfterIt()
    {
        _store.Save(new UpdateCheckState { LastCheck = _time.Now });
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(0, _checks);

        _time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, _checks);
    }

    [Fact]
    public void ANewVersionIsReported()
    {
        Check = _ => Task.FromResult(new UpdateCheckOutcome(UpdateCheckResult.Answered, new ReleaseVersion(0, 2, 0)));
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(UpdateCheckScheduler.StartDelay);

        Assert.Equal([new ReleaseVersion(0, 2, 0)], _found);
    }

    [Fact]
    public void AnAnswerWithoutANewVersionReportsNothing()
    {
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(UpdateCheckScheduler.StartDelay);

        Assert.Equal(1, _checks);
        Assert.Empty(_found);
    }

    [Fact]
    public void OffFromTheStartNothingIsChecked()
    {
        _scheduler.Apply(checkAutomatically: false);

        _time.Advance(TimeSpan.FromDays(3));

        Assert.Equal(0, _checks);
    }

    [Fact]
    public void TurnedOnLaterADueCheckRunsAtOnce()
    {
        _scheduler.Apply(checkAutomatically: false);
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(TimeSpan.Zero);

        Assert.Equal(1, _checks);
    }

    [Fact]
    public void TurnedOffNoMoreChecksRun()
    {
        Check = _ => Task.FromResult(NoAnswer);
        _scheduler.Apply(checkAutomatically: true);
        _time.Advance(UpdateCheckScheduler.StartDelay);

        _scheduler.Apply(checkAutomatically: false);
        _time.Advance(TimeSpan.FromDays(2));

        Assert.Equal(1, _checks);
    }

    [Fact]
    public void ApplyingTheSameSettingAgainKeepsTheSchedule()
    {
        _scheduler.Apply(checkAutomatically: true);
        _time.Advance(TimeSpan.FromMinutes(1));

        _scheduler.Apply(checkAutomatically: true);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, _checks);
    }

    [Fact]
    public void TurningItOffCancelsACheckUnderWay()
    {
        var token = StartCheckThatWaits();

        _scheduler.Apply(checkAutomatically: false);

        Assert.True(token.IsCancellationRequested);
        Assert.Contains((LogLevel.Debug, "The update check was turned off while it ran."), _logger.Entries);
        Assert.Empty(_found);
    }

    [Fact]
    public void DisposingCancelsACheckUnderWayAndStopsTheLooks()
    {
        var token = StartCheckThatWaits();

        _scheduler.Dispose();
        _time.Advance(TimeSpan.FromDays(2));

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(1, _checks);
    }

    [Fact]
    public void OnlyOneCheckRunsAtATime()
    {
        StartCheckThatWaits();

        _time.Advance(TimeSpan.FromHours(3));

        Assert.Equal(1, _checks);
    }

    [Fact]
    public void AFailedCheckIsLoggedAndTriedAgainAnHourLater()
    {
        Check = _ => Task.FromException<UpdateCheckOutcome>(new InvalidOperationException("Broken."));
        _scheduler.Apply(checkAutomatically: true);

        _time.Advance(UpdateCheckScheduler.StartDelay);
        Assert.Contains((LogLevel.Warning, "The update check failed."), _logger.Entries);

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, _checks);
    }

    // Starts a check that runs until it's canceled, and returns its cancellation token.
    private CancellationToken StartCheckThatWaits()
    {
        var token = CancellationToken.None;
        Check = cancellationToken =>
        {
            token = cancellationToken;
            var pending = new TaskCompletionSource<UpdateCheckOutcome>();
            cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return pending.Task;
        };

        _scheduler.Apply(checkAutomatically: true);
        _time.Advance(UpdateCheckScheduler.StartDelay);
        Assert.Equal(1, _checks);
        return token;
    }
}
