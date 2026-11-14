using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// Keeps <c>PCCompanion.RemoteFolders.exe</c> running while Remote Folders is on: starts it, and
/// after it ends unexpectedly, starts it again after 1, 10 and 60 seconds; after a fourth such end
/// within an hour, it gives up until the settings change. See "Threads and processes" in
/// docs/features/files/remote-folders.md.
/// </summary>
/// <param name="start">Starts the provider; it may throw <see cref="Win32Exception"/>.</param>
/// <param name="askToStop">Asks the running provider to stop.</param>
public sealed partial class ProviderSupervisor(Func<RunningProcess> start, Action askToStop, TimeProvider time, ILogger logger) : IDisposable
{
    /// <summary>How long it waits before each start after an unexpected end: after the first, the second, and the third and later.</summary>
    public static readonly IReadOnlyList<TimeSpan> RestartDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)];

    /// <summary>After this many unexpected ends within <see cref="CrashWindow"/>, it gives up.</summary>
    public const int MaximumCrashes = 4;

    public static readonly TimeSpan CrashWindow = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _crashes = [];
    private CancellationTokenSource _waiting = new();
    private bool _wanted;
    private int _stops;
    private bool _busy;
    private bool _gaveUp;
    private bool _disposed;

    /// <summary>Raised on the thread pool when the provider ended unexpectedly too often, so it isn't started again until the settings change.</summary>
    public event EventHandler? GaveUp;

    /// <summary>Whether it gave up after too many unexpected ends.</summary>
    public bool HasGivenUp
    {
        get
        {
            lock (_gate)
            {
                return _gaveUp;
            }
        }
    }

    /// <summary>The provider that runs now, if any.</summary>
    public int? ProcessId { get; private set; }

    /// <summary>
    /// Has the provider run, starting it on the thread pool if it doesn't. After giving up, it
    /// starts again only when <paramref name="settingsChanged"/>. Doesn't wait.
    /// </summary>
    public void Run(bool settingsChanged)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _wanted = true;
            if (settingsChanged && _gaveUp)
            {
                _gaveUp = false;
                _crashes.Clear();
            }

            if (_gaveUp || _busy)
            {
                return;
            }

            _busy = true;
        }

        _ = Task.Run(StartAsync);
    }

    /// <summary>Stops starting the provider and asks the running one to stop. Doesn't wait.</summary>
    public void Stop()
    {
        CancellationTokenSource waiting;
        lock (_gate)
        {
            _wanted = false;
            _stops++;
            waiting = _waiting;
            _waiting = new CancellationTokenSource();
        }

        waiting.Cancel();
        waiting.Dispose();
        askToStop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    private async Task StartAsync()
    {
        int stops;
        lock (_gate)
        {
            stops = _stops;
        }

        RunningProcess process;
        try
        {
            process = start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            LogStartFailed(logger, exception);
            await EndedAsync(exitCode: null, stops).ConfigureAwait(false);
            return;
        }

        ProcessId = process.Id;
        LogStarted(logger, process.Id);
        int exitCode;
        using (process)
        {
            exitCode = await process.Exited.ConfigureAwait(false);
        }

        ProcessId = null;
        await EndedAsync(exitCode, stops).ConfigureAwait(false);
    }

    // An end the host didn't ask for is a crash, whatever its exit code; one it asked for while
    // it wanted the provider again, as when Remote Folders is turned off and on, starts it at once.
    private async Task EndedAsync(int? exitCode, int stops)
    {
        TimeSpan delay;
        CancellationToken waiting;
        bool gaveUp = false;
        lock (_gate)
        {
            if (!_wanted || _disposed)
            {
                _busy = false;
                LogEnded(logger, exitCode);
                return;
            }

            if (_stops != stops)
            {
                LogEnded(logger, exitCode);
                _ = Task.Run(StartAsync);
                return;
            }

            var now = time.GetUtcNow();
            _crashes.Add(now);
            _crashes.RemoveAll(crash => now - crash > CrashWindow);
            if (_crashes.Count >= MaximumCrashes)
            {
                _gaveUp = true;
                _busy = false;
                gaveUp = true;
            }

            delay = RestartDelays[Math.Min(_crashes.Count, RestartDelays.Count) - 1];
            waiting = _waiting.Token;
        }

        if (gaveUp)
        {
            LogGaveUp(logger, exitCode, MaximumCrashes);
            GaveUp?.Invoke(this, EventArgs.Empty);
            return;
        }

        LogRestarting(logger, exitCode, delay.TotalSeconds);
        try
        {
            await Task.Delay(delay, time, waiting).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        lock (_gate)
        {
            if (!_wanted || _disposed)
            {
                _busy = false;
                return;
            }
        }

        await StartAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Remote Folders' process started, process {ProcessId}.")]
    private static partial void LogStarted(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Remote Folders' process stopped (exit code {ExitCode}).")]
    private static partial void LogEnded(ILogger logger, int? exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote Folders' process couldn't be started.")]
    private static partial void LogStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote Folders' process ended unexpectedly (exit code {ExitCode}); it starts again in {Seconds} seconds.")]
    private static partial void LogRestarting(ILogger logger, int? exitCode, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Remote Folders' process ended unexpectedly (exit code {ExitCode}), the {Count}th time within an hour, so it isn't started again until the settings change.")]
    private static partial void LogGaveUp(ILogger logger, int? exitCode, int count);
}
