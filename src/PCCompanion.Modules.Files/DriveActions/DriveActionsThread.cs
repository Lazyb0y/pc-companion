using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// Runs Drive Actions on a <see cref="MessageLoopThread{TSettings}"/> of its own, at background
/// priority, as "Threads" in docs/features/files/drive-actions.md says: a <see cref="DriveActionsRunner"/>
/// recognizes the drives and runs their actions there, one file at a time. The host's UI thread
/// starts it, passes it Windows' messages and new settings, and stops it, and never waits for it,
/// except for <see cref="MessageLoopThread{TSettings}.Join"/> when the host exits.
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
    private readonly ILogger _logger;
    private volatile MessageWindow? _window;
    private volatile DriveActionsRunner? _runner;
    private bool _applied;

    /// <param name="host">Makes what the runner asks of the host's UI thread, once the thread has its window.</param>
    /// <param name="previous">A thread that was asked to stop, which this one waits for first, so that they don't share the history.</param>
    public DriveActionsThread(
        DriveActionsSettings settings,
        IDriveActionsSystem system,
        Func<DriveActionsThread, IDriveActionsHost> host,
        DriveRecordsStore records,
        DriveActionsHistoryStore history,
        SpecialFolders places,
        TimeProvider time,
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
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _host = host;
        _records = records;
        _history = history;
        _places = places;
        _time = time;
        _logger = logger;
        Start();
    }

    /// <summary>
    /// Stops the copy of the run on the drive with <paramref name="letter"/>, from the host's UI
    /// thread, as Windows asks to eject the drive, which a request to the thread would reach only
    /// once the copy is done.
    /// </summary>
    public void CancelRunOn(char letter)
    {
        if (_runner?.Running is { } run && run.Letter == letter)
        {
            run.Stop(RunOutcome.Ejected);
        }
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
        return new Closing(runner);
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

    private sealed class Closing(DriveActionsRunner runner) : IDisposable
    {
        public void Dispose() => runner.Close();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drive Actions started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drive Actions' thread stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drive Actions failed, so it's off until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
