using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// Runs Drive Actions on a <see cref="MessageLoopThread{TSettings}"/> of its own, at background
/// priority, as "Threads" in docs/features/files/drive-actions.md says: a <see cref="DriveActionsRunner"/>
/// recognizes the drives and runs their actions there, one file at a time, and answers the
/// settings app's requests. The host's UI thread starts it, passes it Windows' messages and new
/// settings, and stops it, and never waits for it, except for
/// <see cref="MessageLoopThread{TSettings}.Join"/> when the host exits.
/// </summary>
public sealed partial class DriveActionsThread : MessageLoopThread<DriveActionsSettings>
{
    private const string WindowClassName = "PCCompanion.DriveActions";

    private readonly IDriveActionsSystem _system;
    private readonly Func<DriveActionsThread, IDriveActionsHost> _host;
    private readonly DriveRecordsStore _records;
    private readonly DriveActionsHistoryStore _history;
    private readonly SpecialFolders _places;
    private readonly TimeProvider _time;
    private readonly string _pipeName;
    private readonly ILogger _logger;
    private volatile MessageWindow? _window;
    private volatile DriveActionsRunner? _runner;
    private bool _applied;

    /// <param name="host">Makes what the runner asks of the host's UI thread, once the thread has its window.</param>
    /// <param name="pipeName">The pipe through which the settings app asks for the drives, the history, Run now and Undo; <see cref="DriveActionsPipe.Name"/>.</param>
    /// <param name="previous">A thread that was asked to stop, which this one waits for first, so that they don't share the history.</param>
    public DriveActionsThread(
        DriveActionsSettings settings,
        IDriveActionsSystem system,
        Func<DriveActionsThread, IDriveActionsHost> host,
        DriveRecordsStore records,
        DriveActionsHistoryStore history,
        SpecialFolders places,
        TimeProvider time,
        string pipeName,
        ILogger logger,
        DriveActionsThread? previous = null)
        : base("Drive Actions", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _host = host;
        _records = records;
        _history = history;
        _places = places;
        _time = time;
        _pipeName = pipeName;
        _logger = logger;
        Start();
    }

    /// <summary>
    /// Stops the copy of the run on the drive with <paramref name="letter"/>, from the host's UI
    /// thread, as Windows asks to eject the drive, which a request to the thread would reach only
    /// once the copy is done. Returns what's set once the copy has let go of its file, or
    /// <see langword="null"/> when no run is on the drive.
    /// </summary>
    public ManualResetEventSlim? CancelRunOn(char letter)
    {
        if (_runner?.Running is { } run && run.Letter == letter)
        {
            run.Stop(RunOutcome.Ejected);
            return run.Idle;
        }

        return null;
    }

    /// <summary>Runs <paramref name="action"/> with the runner on the thread, from any thread, without waiting.</summary>
    public void Send(Action<DriveActionsRunner> action) =>
        Post(() =>
        {
            if (_runner is { } runner)
            {
                action(runner);
            }
        });

    /// <summary>Runs <paramref name="action"/> on the thread after the messages waiting there.</summary>
    internal void Later(Action action) => _window?.BeginInvoke(() => Guard(action));

    protected override IDisposable? Open(MessageWindow window)
    {
        _system.EnterBackgroundMode();
        _window = window;
        var runner = new DriveActionsRunner(_system, _host(this), _records, _history, _places, _time, _logger);
        _runner = runner;
        return new Closing(runner, Serve());
    }

    protected override void Apply(DriveActionsSettings settings)
    {
        var first = !_applied;
        _applied = true;
        _runner!.Apply(settings);
        if (first)
        {
            // The drives connected before Drive Actions could see them are asked about.
            _runner.LookAtConnectedDrives();
        }
    }

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    // The settings app's requests come through the pipe on the thread pool and are answered on
    // this thread, which has the drives and the history. While the host runs as administrator
    // there's no thread, and no pipe.
    private RequestPipe? Serve()
    {
        try
        {
            return RequestPipe.Serve(_pipeName, DriveActionsPipe.MaximumRequestLength, AnswerAsync, exception => LogPipeFailed(_logger, exception.HResult));
        }
        catch (Win32Exception exception)
        {
            LogPipeNotServed(_logger, exception.NativeErrorCode);
            return null;
        }
    }

    // On the thread pool. A request that can't be read, or that the thread can't take because it
    // ended or failed, is refused.
    private Task<byte[]> AnswerAsync(byte[] request)
    {
        var refused = DriveActionsPipe.Write(new DriveActionsAnswer { Status = DriveActionsAnswerStatus.Refused });
        if (DriveActionsPipe.ReadRequest(request) is not { } read || _window is not { } window)
        {
            return Task.FromResult(refused);
        }

        var answered = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = window.BeginInvoke(() =>
        {
            var taken = false;
            Guard(() =>
            {
                if (_runner is { } runner)
                {
                    runner.Answer(read, answer => answered.TrySetResult(DriveActionsPipe.Write(answer)));
                    taken = true;
                }
            });

            if (!taken)
            {
                answered.TrySetResult(refused);
            }
        });

        if (!posted)
        {
            answered.TrySetResult(refused);
        }

        return answered.Task;
    }

    // The pipe goes first, so that no request reaches the runner once it's closed.
    private sealed class Closing(DriveActionsRunner runner, RequestPipe? pipe) : IDisposable
    {
        public void Dispose()
        {
            pipe?.Dispose();
            runner.Close();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drive Actions started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drive Actions' thread stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drive Actions failed, so it's off until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drive Actions' pipe for the settings app couldn't be created (error {ErrorCode}), so the page can't show the drives or the history.")]
    private static partial void LogPipeNotServed(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drive Actions' pipe for the settings app failed (error {ErrorCode}).")]
    private static partial void LogPipeFailed(ILogger logger, int errorCode);
}
