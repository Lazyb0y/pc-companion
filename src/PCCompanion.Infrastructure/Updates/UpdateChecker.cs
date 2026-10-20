using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Infrastructure.Updates;

public enum UpdateCheckResult
{
    /// <summary>GitHub answered less than a day ago, so nothing was asked.</summary>
    NotDue,

    /// <summary>The request got no answer; it doesn't count as the day's check.</summary>
    NoAnswer,

    /// <summary>GitHub answered, which counts as the day's check.</summary>
    Answered,
}

/// <param name="Result">What happened.</param>
/// <param name="NewVersion">
/// A newer version that the notification hasn't shown yet. It's saved as shown, so the caller
/// shows the notification for it.
/// </param>
public sealed record UpdateCheckOutcome(UpdateCheckResult Result, ReleaseVersion? NewVersion = null);

public enum CheckNowResult
{
    /// <summary>GitHub named no version newer than the running one.</summary>
    UpToDate,

    /// <summary>GitHub named a newer version.</summary>
    UpdateAvailable,

    /// <summary>The request got no answer.</summary>
    NoAnswer,

    /// <summary>GitHub answered with an error status, such as a rate limit, or with an answer that can't be used.</summary>
    Failed,
}

/// <param name="Result">What the check found.</param>
/// <param name="NewerVersion">The newer version, for <see cref="CheckNowResult.UpdateAvailable"/>.</param>
public sealed record CheckNowOutcome(CheckNowResult Result, ReleaseVersion? NewerVersion = null);

/// <summary>
/// Runs one update check: asks GitHub when a check is due, or when the user selects Check now,
/// and saves what it answered.
/// </summary>
public sealed partial class UpdateChecker(UpdateCheckStore store, GitHubReleaseSource source, TimeProvider time, ILogger<UpdateChecker> logger)
{
    /// <summary>How long after GitHub's last answer the next check is due.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether a check is due at <paramref name="now"/>: before the first answer, a day after the
    /// last one, or when the last one seems to be in the future because the clock moved back.
    /// </summary>
    public static bool IsDue(UpdateCheckState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.LastCheck is not { } last || last > now || now - last >= Interval;
    }

    /// <summary>
    /// What a check run by a copy of the host without admin rights did, from the result before and
    /// after it: an answer when the time of the last answer changed, and no answer otherwise. A
    /// version newly saved as shown is the one to show the notification for. Any program running
    /// as the user can change the file, so only a version newer than <paramref name="current"/> is shown.
    /// </summary>
    public static UpdateCheckOutcome OutcomeOfCopy(UpdateCheckState before, UpdateCheckState after, ReleaseVersion current)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (after.LastCheck is null || after.LastCheck == before.LastCheck)
        {
            return new UpdateCheckOutcome(UpdateCheckResult.NoAnswer);
        }

        ReleaseVersion? newVersion = null;
        if (after.NotifiedVersion is { } notified && notified != before.NotifiedVersion && notified > current)
        {
            newVersion = notified;
        }

        return new UpdateCheckOutcome(UpdateCheckResult.Answered, newVersion);
    }

    /// <param name="current">The running version.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<UpdateCheckOutcome> CheckAsync(ReleaseVersion current, CancellationToken cancellationToken)
    {
        var state = store.Load();
        if (!IsDue(state, time.GetUtcNow()))
        {
            return new UpdateCheckOutcome(UpdateCheckResult.NotDue);
        }

        return (await AskAsync(state, current, cancellationToken).ConfigureAwait(false)).Outcome;
    }

    /// <summary>
    /// Runs the check the user asked for with Check now on the General page: asks GitHub whether
    /// or not a check is due, and saves the answer as the day's check. A newer version is saved as
    /// shown, since the General page shows it, so the notification doesn't show it later.
    /// </summary>
    /// <param name="current">The running version.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<CheckNowOutcome> CheckNowAsync(ReleaseVersion current, CancellationToken cancellationToken)
    {
        var (_, answer, latest) = await AskAsync(store.Load(), current, cancellationToken).ConfigureAwait(false);
        return answer switch
        {
            ReleaseAnswerKind.NoAnswer => new CheckNowOutcome(CheckNowResult.NoAnswer),
            ReleaseAnswerKind.ErrorStatus or ReleaseAnswerKind.Unreadable => new CheckNowOutcome(CheckNowResult.Failed),
            _ when latest is { } version && version > current => new CheckNowOutcome(CheckNowResult.UpdateAvailable, version),
            _ => new CheckNowOutcome(CheckNowResult.UpToDate),
        };
    }

    // Asks GitHub and saves what it answered. Also returns what GitHub answered and the newest
    // version known after it.
    private async Task<(UpdateCheckOutcome Outcome, ReleaseAnswerKind Answer, ReleaseVersion? Latest)> AskAsync(
        UpdateCheckState state, ReleaseVersion current, CancellationToken cancellationToken)
    {
        LogChecking(logger);
        var answer = await source.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        var latest = state.LatestVersion;
        switch (answer.Kind)
        {
            case ReleaseAnswerKind.NoAnswer:
                LogNoAnswer(logger, answer.Exception, answer.Problem);
                return (new UpdateCheckOutcome(UpdateCheckResult.NoAnswer), answer.Kind, latest);
            case ReleaseAnswerKind.Release:
                latest = answer.Version;
                break;
            case ReleaseAnswerKind.NoRelease:
                LogNoRelease(logger);
                latest = null;
                break;
            case ReleaseAnswerKind.ErrorStatus:
                LogErrorStatus(logger, answer.StatusCode);
                break;
            default:
                LogUnreadable(logger, answer.Exception, answer.StatusCode, answer.Problem);
                break;
        }

        if (answer.Version is { } named)
        {
            if (named > current)
            {
                LogNewerVersion(logger, named, current);
            }
            else
            {
                LogUpToDate(logger, current);
            }
        }

        ReleaseVersion? newVersion = null;
        if (latest is { } version && version > current && (state.NotifiedVersion is not { } notified || version > notified))
        {
            newVersion = version;
        }

        // Saved before the notification shows, so it shows once per version. A result that can't
        // be saved still counts as the day's check while the host runs.
        var now = time.GetUtcNow();
        try
        {
            store.Save(state with { LastCheck = now, LatestVersion = latest, NotifiedVersion = newVersion ?? state.NotifiedVersion });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantSave(logger, exception);
        }

        LogNextCheck(logger, now + Interval);
        return (new UpdateCheckOutcome(UpdateCheckResult.Answered, newVersion), answer.Kind, latest);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Checking for updates.")]
    private static partial void LogChecking(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The update check got no answer from GitHub ({Problem}).")]
    private static partial void LogNoAnswer(ILogger logger, Exception? exception, string? problem);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GitHub has no release of PC Companion yet.")]
    private static partial void LogNoRelease(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub answered the update check with status {StatusCode}.")]
    private static partial void LogErrorStatus(ILogger logger, int? statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub's answer to the update check (status {StatusCode}) can't be used: {Problem}.")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception, int? statusCode, string? problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "PC Companion {Version} is available; this is {Current}.")]
    private static partial void LogNewerVersion(ILogger logger, ReleaseVersion version, ReleaseVersion current);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PC Companion {Current} is up to date.")]
    private static partial void LogUpToDate(ILogger logger, ReleaseVersion current);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't save the update check's result.")]
    private static partial void LogCantSave(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The next update check is due at {Time:u}.")]
    private static partial void LogNextCheck(ILogger logger, DateTimeOffset time);
}
