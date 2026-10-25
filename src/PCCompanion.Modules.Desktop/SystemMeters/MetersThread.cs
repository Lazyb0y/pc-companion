using System;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Runs the meters on a <see cref="MessageLoopThread{TSettings}"/> of their own, as "Threads" in
/// docs/features/desktop/system-meters.md says. The host's UI thread starts it, passes it new
/// settings and stops it, and never waits for it, except for <see cref="Join"/> when the host
/// exits. Updates pause while the session is locked and while the displays are off.
/// </summary>
public sealed partial class MetersThread : MessageLoopThread<SystemMetersSettings>
{
    private const string WindowClassName = "PCCompanion.SystemMeters";

    private readonly IMetersSystem _system;
    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    // The meters thread's part, while its message loop runs: Open creates it and the base class
    // disposes it, so this is only where Apply finds it.
    private Meters? _meters;

    /// <summary>Starts the thread, which shows the meters <paramref name="settings"/> turn on.</summary>
    /// <param name="previous">
    /// A thread that was asked to stop, which this one waits for before it creates its window.
    /// </param>
    public MetersThread(SystemMetersSettings settings, IMetersSystem system, IHostCommands commands, TimeProvider time, ILogger logger, MetersThread? previous = null)
        : base("System Meters", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _commands = commands;
        _time = time;
        _logger = logger;
        Start();
    }

    protected override IDisposable Open(MessageWindow window)
    {
        var meters = new Meters(this, window);
        _meters = meters;
        return meters;
    }

    protected override void Apply(SystemMetersSettings settings) => _meters!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    private void OpenTaskManager()
    {
        if (_system.OpenTaskManager())
        {
            LogTaskManagerOpened(_logger);
        }
        else
        {
            LogTaskManagerRefused(_logger);
        }
    }

    private void ShowMenu(MessageWindow window, int x, int y)
    {
        var command = _system.ShowMenu(window, x, y);
        LogMenuClosed(_logger, command);
        switch (command)
        {
            case MeterMenuCommand.TaskManager:
                OpenTaskManager();
                break;
            case MeterMenuCommand.Settings:
                _commands.OpenSettings();
                break;
            case MeterMenuCommand.Exit:
                _commands.ExitApp();
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "System Meters started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "System Meters' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "System Meters applied its settings: CPU {Cpu}, memory {Memory}, network {Network}, every {Seconds} s.")]
    private static partial void LogApplied(ILogger logger, bool cpu, bool memory, bool network, double seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "System Meters paused: {Paused} (session locked {Locked}, displays off {DisplaysOff}).")]
    private static partial void LogPaused(ILogger logger, bool paused, bool locked, bool displaysOff);

    [LoggerMessage(Level = LogLevel.Debug, Message = "System Meters opened Task Manager.")]
    private static partial void LogTaskManagerOpened(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open Task Manager.")]
    private static partial void LogTaskManagerRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Closed the meters' menu; chose {Command}.")]
    private static partial void LogMenuClosed(ILogger logger, MeterMenuCommand command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The taskbar restarted, and Windows took the {Meter} meter's icon again.")]
    private static partial void LogIconAddedAgain(ILogger logger, Meter meter);

    [LoggerMessage(Level = LogLevel.Error, Message = "System Meters failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // What runs on the meters thread: the icons, the timer, and the session and display
    // notifications that pause it.
    private sealed class Meters : IDisposable
    {
        private readonly MetersThread _owner;
        private readonly IMeterIcons? _icons;
        private readonly MetersController? _controller;
        private readonly IMeterPauses? _pauses;
        private CoalescableTimer? _timer;
        private TimeSpan _timerInterval;
        private SystemMetersSettings? _settings;
        private bool _locked;
        private bool _displaysOff;

        public Meters(MetersThread owner, MessageWindow window)
        {
            _owner = owner;
            Window = window;
            try
            {
                _icons = owner._system.CreateIcons(Window);
                _icons.Selected += (_, _) => owner.Guard(owner.OpenTaskManager);
                _icons.MenuRequested += (_, e) => owner.Guard(() => owner.ShowMenu(Window, e.X, e.Y));
                _icons.AddedAgain += (_, e) => owner.Guard(() => LogIconAddedAgain(owner._logger, e.Meter));
                _controller = new MetersController(owner._system.Readings, owner._system.Appearance, _icons, CreateStrip, owner._time, owner._logger);

                // It reports the displays' state at once.
                _pauses = owner._system.WatchPauses(Window);
                _pauses.Locked += (_, _) => owner.Guard(() => SetPaused(locked: true, _displaysOff));
                _pauses.Unlocked += (_, _) => owner.Guard(() => SetPaused(locked: false, _displaysOff));
                _pauses.DisplaysOff += (_, _) => owner.Guard(() => SetPaused(_locked, displaysOff: true));
                _pauses.DisplaysOn += (_, _) => owner.Guard(() => SetPaused(_locked, displaysOff: false));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public MessageWindow Window { get; }

        private bool IsPaused => _locked || _displaysOff;

        public void Apply(SystemMetersSettings settings)
        {
            _settings = settings;
            _controller!.Apply(settings);
            LogApplied(_owner._logger, settings.Cpu, settings.Memory, settings.Network, settings.Interval.TotalSeconds);
            Schedule();
        }

        // Removes the meters first, then lets go of the notifications; the window goes after.
        public void Dispose()
        {
            _timer?.Dispose();
            _controller?.Dispose();
            _icons?.Dispose();
            _pauses?.Dispose();
        }

        // The strip and its watch on the taskbar exist only while the meters show on the taskbar, on a tested build.
        private MeterStrip? CreateStrip()
        {
            if (!MeterStrip.IsSupportedBuild(_owner._system.WindowsBuild))
            {
                return null;
            }

            var host = _owner._system.CreateStripHost(Window, _owner.Guard);
            host.Clicked += (_, _) => _owner.Guard(_owner.OpenTaskManager);
            host.MenuRequested += (_, e) => _owner.Guard(() => _owner.ShowMenu(Window, e.X, e.Y));
            host.PointerOverChanged += (_, _) => _owner.Guard(() => _controller?.RedrawStrip());
            host.LayoutMayHaveChanged += (_, _) => _owner.Guard(() => _controller?.LayOutStrip());
            host.ZOrderMayHaveChanged += (_, _) => _owner.Guard(() => _controller?.KeepStripInSight());
            return new MeterStrip(host, _owner._system.WindowsBuild);
        }

        private void SetPaused(bool locked, bool displaysOff)
        {
            var wasPaused = IsPaused;
            (_locked, _displaysOff) = (locked, displaysOff);
            if (IsPaused != wasPaused)
            {
                LogPaused(_owner._logger, IsPaused, _locked, _displaysOff);
            }

            Schedule();
        }

        // Runs the timer only while someone can see the meters, and pauses the meters, the strip's
        // watch on the taskbar included, while no one can. The meters start their readings over
        // when the timer starts again, so the next values cover only the time since.
        private void Schedule()
        {
            if (IsPaused || _settings is null)
            {
                if (IsPaused)
                {
                    _controller!.Pause();
                }

                _timer?.Dispose();
                _timer = null;
                return;
            }

            var interval = _settings.Interval;
            if (_timer is not null && interval == _timerInterval)
            {
                return;
            }

            if (_timer is null)
            {
                _controller!.Resume();
            }

            _timer?.Dispose();
            _timer = new CoalescableTimer(Window, interval, interval / 10);
            _timer.Elapsed += (_, _) => _owner.Guard(_controller!.Update);
            _timerInterval = interval;
        }
    }
}
