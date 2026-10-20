using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Desktop.SystemMeters;

/// <summary>
/// Runs the meters on a thread of their own, with its own hidden window and message loop, as
/// "Threads" in docs/features/desktop/system-meters.md says. The host's UI thread starts it,
/// passes it new settings and stops it, and never waits for it, except for <see cref="Join"/>
/// when the host exits. Updates pause while the session is locked and while the displays are off.
/// </summary>
public sealed partial class MetersThread
{
    private const string WindowClassName = "PCCompanion.SystemMeters";

    private readonly IMetersSystem _system;
    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly MetersThread? _previous;
    private readonly Thread _thread;

    // Shared with the host's UI thread.
    private readonly Lock _gate = new();
    private SystemMetersSettings _latest;
    private MessageWindow? _window;
    private bool _stopping;
    private volatile bool _failed;

    // The meters thread's part, while its message loop runs; Run creates and disposes it.
    private Meters? _meters;

    /// <summary>Starts the thread, which shows the meters <paramref name="settings"/> turn on.</summary>
    /// <param name="previous">
    /// A thread that was asked to stop, which this one waits for before it creates its window.
    /// </param>
    public MetersThread(SystemMetersSettings settings, IMetersSystem system, IHostCommands commands, TimeProvider time, ILogger logger, MetersThread? previous = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _latest = settings;
        _system = system;
        _commands = commands;
        _time = time;
        _logger = logger;
        _previous = previous;

        // A background thread, so one that doesn't end doesn't keep the host running.
        _thread = new Thread(Run) { IsBackground = true, Name = "System Meters" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Whether the thread stopped because of a failure, which it logged.</summary>
    public bool HasFailed => _failed;

    private SystemMetersSettings Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Passes new settings to the thread, without waiting for it. Call it from one thread.</summary>
    public void Update(SystemMetersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MessageWindow? window;
        lock (_gate)
        {
            _latest = settings;
            window = _window;
        }

        // Without a window yet, the thread takes the latest settings when it creates one.
        window?.BeginInvoke(() => Guard(() => _meters?.Apply(Latest)));
    }

    /// <summary>Asks the thread to remove the meters and end, without waiting for it.</summary>
    public void Stop()
    {
        MessageWindow? window;
        lock (_gate)
        {
            _stopping = true;
            window = _window;
        }

        window?.BeginInvoke(MessageLoop.Quit);
    }

    /// <summary>Waits for the thread to end. Returns false when it didn't in time.</summary>
    public bool Join(TimeSpan timeout) => _thread.Join(timeout);

    private void Run()
    {
        try
        {
            _previous?.Join(Timeout.InfiniteTimeSpan);

            using var meters = new Meters(this);
            try
            {
                if (Attach(meters))
                {
                    meters.Apply(Latest);
                    LogStarted(_logger);
                    MessageLoop.Run();
                }
            }
            finally
            {
                Detach();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }

        LogStopped(_logger);
    }

    // Returns false when the thread was asked to stop before it had a window.
    private bool Attach(Meters meters)
    {
        _meters = meters;
        lock (_gate)
        {
            _window = meters.Window;
            return !_stopping;
        }
    }

    private void Detach()
    {
        lock (_gate)
        {
            _window = null;
        }

        _meters = null;
    }

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

    // Runs on the meters thread, where an exception would end the host. A failure ends the
    // thread, which removes the meters.
    private void Guard(Action action)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Fail(exception);
            MessageLoop.Quit();
        }
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        LogFailed(_logger, exception);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "System Meters failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // What runs on the meters thread: its window, the icons, the timer, and the session and
    // display notifications that pause it.
    private sealed class Meters : IDisposable
    {
        private readonly MetersThread _owner;
        private readonly IMeterIcons? _icons;
        private readonly MetersController? _controller;
        private readonly SessionMonitor? _sessions;
        private readonly DisplayStateMonitor? _displays;
        private CoalescableTimer? _timer;
        private TimeSpan _timerInterval;
        private SystemMetersSettings? _settings;
        private bool _locked;
        private bool _displaysOff;

        public Meters(MetersThread owner)
        {
            _owner = owner;
            Window = new MessageWindow(WindowClassName);
            try
            {
                _icons = owner._system.CreateIcons(Window);
                _icons.Selected += (_, _) => owner.Guard(owner.OpenTaskManager);
                _icons.MenuRequested += (_, e) => owner.Guard(() => owner.ShowMenu(Window, e.X, e.Y));
                _controller = new MetersController(owner._system.Readings, owner._system.Appearance, _icons, CreateStrip, owner._time, owner._logger);

                _sessions = new SessionMonitor(Window);
                _sessions.Locked += (_, _) => owner.Guard(() => SetPaused(locked: true, _displaysOff));
                _sessions.Unlocked += (_, _) => owner.Guard(() => SetPaused(locked: false, _displaysOff));

                // It reports the displays' state at once.
                _displays = new DisplayStateMonitor(Window);
                _displays.TurnedOff += (_, _) => owner.Guard(() => SetPaused(_locked, displaysOff: true));
                _displays.TurnedOn += (_, _) => owner.Guard(() => SetPaused(_locked, displaysOff: false));
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
            if (settings == _settings)
            {
                return;
            }

            _settings = settings;
            _controller!.Apply(settings);
            LogApplied(_owner._logger, settings.Cpu, settings.Memory, settings.Network, settings.Interval.TotalSeconds);
            Schedule();
        }

        // Removes the meters first, then lets go of the notifications and the window.
        public void Dispose()
        {
            _timer?.Dispose();
            _controller?.Dispose();
            _icons?.Dispose();
            _displays?.Dispose();
            _sessions?.Dispose();
            Window.Dispose();
        }

        // The strip and its watch on the taskbar exist only while the meters show on the taskbar, on a tested build.
        private MeterStrip? CreateStrip()
        {
            if (!MeterStrip.IsSupportedBuild(_owner._system.WindowsBuild))
            {
                return null;
            }

            var host = _owner._system.CreateStripHost(Window);
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

        // Runs the timer only while someone can see the meters. The meters start their readings
        // over when the timer starts again, so the next values cover only the time since.
        private void Schedule()
        {
            if (IsPaused || _settings is null)
            {
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
