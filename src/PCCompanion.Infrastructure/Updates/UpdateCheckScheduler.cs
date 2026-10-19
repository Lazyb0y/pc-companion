using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Infrastructure.Updates;

public sealed class UpdateFoundEventArgs(ReleaseVersion version) : EventArgs
{
    /// <summary>The newer version, which the notification shows.</summary>
    public ReleaseVersion Version { get; } = version;
}

/// <summary>
/// Decides when the update check runs while the host runs: 2 minutes after the host starts, then
/// whenever an hourly look finds a check due. Checks run on the thread pool, one at a time.
/// </summary>
public sealed partial class UpdateCheckScheduler : IDisposable
{
    /// <summary>How long after the host starts the first look waits, so it doesn't slow down signing in.</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);

    /// <summary>How often the scheduler looks whether a check is due. Sleep delays a check by this much at most.</summary>
    public static readonly TimeSpan LookInterval = TimeSpan.FromHours(1);

    private readonly Func<CancellationToken, Task<UpdateCheckOutcome>> _check;
    private readonly UpdateCheckStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateCheckScheduler> _logger;
    private readonly Lock _lock = new();
    private ITimer? _timer;
    private CancellationTokenSource? _running;
    private bool _applied;
    private bool _on;
    private bool _disposed;

    // Used only by the check that runs, one at a time.
    private DateTimeOffset? _lastAnswer;
    private bool _reportedNoAnswer;

    /// <param name="check">Runs one check, in this process or in a copy of the host without admin rights.</param>
    /// <param name="store">Says when GitHub last answered, so a check runs at most once a day.</param>
    public UpdateCheckScheduler(Func<CancellationToken, Task<UpdateCheckOutcome>> check, UpdateCheckStore store, TimeProvider time, ILogger<UpdateCheckScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _check = check;
        _store = store;
        _time = time;
        _logger = logger;
    }

    /// <summary>Raised, on a thread-pool thread, when a check finds a version the notification hasn't shown.</summary>
    public event EventHandler<UpdateFoundEventArgs>? UpdateFound;

    /// <summary>
    /// Starts or stops the checks, from the setting; call it at startup and after every settings
    /// change. Turned on after startup, a due check runs at once. Turned off, a check under way is
    /// canceled.
    /// </summary>
    public void Apply(bool checkAutomatically)
    {
        CancellationTokenSource? cancel = null;
        lock (_lock)
        {
            if (_disposed || (_applied && checkAutomatically == _on))
            {
                return;
            }

            var starting = !_applied;
            _applied = true;
            _on = checkAutomatically;
            _timer?.Dispose();
            _timer = null;

            if (checkAutomatically)
            {
                var due = starting ? StartDelay : TimeSpan.Zero;
                _timer = _time.CreateTimer(_ => Look(), null, due, LookInterval);
                LogOn(_logger, due);
            }
            else
            {
                cancel = _running;
                LogOff(_logger);
            }
        }

        // Outside the lock: canceling can run the end of the check on this thread.
        TryCancel(cancel);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancel;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            cancel = _running;
        }

        TryCancel(cancel);
    }

    private static void TryCancel(CancellationTokenSource? running)
    {
        try
        {
            running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The check ended in the meantime.
        }
    }

    private void Look()
    {
        CancellationTokenSource running;
        lock (_lock)
        {
            if (!_on || _disposed || _running is not null)
            {
                return;
            }

            running = new CancellationTokenSource();
            _running = running;
        }

        _ = RunAsync(running);
    }

    // Catches everything, so a failed check never stops the host.
    private async Task RunAsync(CancellationTokenSource running)
    {
        try
        {
            // The answer's time is also kept here, so a result that can't be saved doesn't make
            // the host ask again every hour.
            var now = _time.GetUtcNow();
            if ((_lastAnswer is { } last && last <= now && now - last < UpdateChecker.Interval) || !UpdateChecker.IsDue(_store.Load(), now))
            {
                return;
            }

            var outcome = await _check(running.Token).ConfigureAwait(false);
            switch (outcome.Result)
            {
                case UpdateCheckResult.NoAnswer when _reportedNoAnswer:
                    LogStillNoAnswer(_logger);
                    break;
                case UpdateCheckResult.NoAnswer:
                    _reportedNoAnswer = true;
                    LogNoAnswer(_logger);
                    break;
                case UpdateCheckResult.Answered:
                    _lastAnswer = _time.GetUtcNow();
                    if (outcome.NewVersion is { } version && !running.IsCancellationRequested)
                    {
                        UpdateFound?.Invoke(this, new UpdateFoundEventArgs(version));
                    }

                    break;
            }
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            LogCanceled(_logger);
        }
        catch (Exception exception)
        {
            LogFailed(_logger, exception);
        }
        finally
        {
            lock (_lock)
            {
                _running = null;
            }

            running.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The update check is on; the first look is in {Delay}.")]
    private static partial void LogOn(ILogger logger, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The update check is off.")]
    private static partial void LogOff(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Can't reach GitHub to check for updates; trying again in an hour.")]
    private static partial void LogNoAnswer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Still can't reach GitHub to check for updates; trying again in an hour.")]
    private static partial void LogStillNoAnswer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The update check was turned off while it ran.")]
    private static partial void LogCanceled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update check failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
