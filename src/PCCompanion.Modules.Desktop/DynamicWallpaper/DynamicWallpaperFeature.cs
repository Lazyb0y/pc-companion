using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Desktop.DynamicWallpaper;

/// <summary>
/// Changes the desktop's picture with the sun, the clock or the Windows mode. See
/// docs/features/desktop/dynamic-wallpaper.md.
/// </summary>
public sealed partial class DynamicWallpaperFeature(IDynamicWallpaperSystem system, TimeProvider time, ILogger<DynamicWallpaperFeature> logger) : IFeature
{
    /// <summary>How long a job's call to Explorer may wait before it's cancelled.</summary>
    public static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(10);

    // How long the settings app's Resume waits for the wallpaper thread, which takes well under a second.
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    // How long the host waits as it exits for the wallpaper thread to finish its job.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private Session? _session;
    private IWallpaperWorker? _stopped;
    private DynamicWallpaperSettings _settings = new();
    private WallpaperPlan? _plan;
    private bool _failed;
    private bool _leftoverLooked;

    // Whether a look at a change of the background is waiting on the wallpaper thread.
    private int _checkWaiting;

    // The latest look the UI thread asked for; the wallpaper thread skips older ones.
    private int _look;

    public DynamicWallpaperFeature(DataFolder dataFolder, TimeProvider time, ILogger<DynamicWallpaperFeature> logger)
        : this(new WindowsDynamicWallpaperSystem(dataFolder), time, logger)
    {
    }

    public static FeatureInfo Definition { get; } = new("dynamicWallpaper", DesktopModule.Id, "DynamicWallpaperName", "DynamicWallpaperDescription", "");

    /// <summary>
    /// Whether this PC can run Dynamic Wallpaper, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _session is not null;

    /// <summary>The period in use and what's in the way, for the page; <see langword="null"/> while off.</summary>
    public WallpaperReport? Report => _session?.Applier.Report;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        DynamicWallpaperSettings read;
        try
        {
            read = DynamicWallpaperSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        foreach (var name in DynamicWallpaperSettings.UnknownChoices(settings))
        {
            LogUnknownChoice(logger, name);
        }

        if (!read.Enabled)
        {
            TurnOff();
            return;
        }

        _settings = read;
        if (_session is null)
        {
            Start();
        }

        LogApplied(logger, read.ChangeWith, read.PicturesForEachDisplay ? read.Displays.Count : 0);
        Look();
    }

    /// <summary>
    /// Changes the background again after the feature stepped aside, as <b>Resume</b> on the page
    /// asks, from any thread.
    /// </summary>
    public void Resume() => _ = ResumeAsync();

    // Resumes, and completes once the wallpaper thread has, or when the feature stops first.
    private Task ResumeAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_session is not { } session)
        {
            done.SetResult();
            return done.Task;
        }

        session.Window.Post(() => Guard(() =>
        {
            if (_plan is { } plan && _session == session)
            {
                var settings = _settings;
                Post(() => session.Applier.Resume(settings, plan));
            }

            Post(() => done.TrySetResult());
        }));
        return done.Task;
    }

    // On the thread pool: a request from the settings app, answered with the feature's state.
    private async Task<byte[]> AnswerAsync(WallpaperApplier applier, byte[] request)
    {
        if (DynamicWallpaperPipe.ReadRequest(request) is { Kind: DynamicWallpaperRequestKind.Resume })
        {
            try
            {
                await ResumeAsync().WaitAsync(AnswerTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Turned off meanwhile, or Explorer didn't answer: the state says which.
            }
        }

        var report = applier.Report;
        return DynamicWallpaperPipe.Write(new DynamicWallpaperAnswer
        {
            Running = _session?.Applier == applier,
            Period = report.Period,
            Block = report.Block,
            Issues = report.Issues,
            Spotlight = report.Spotlight,
        });
    }

    // The host disposes features twice as it exits, so the thread is waited for only once.
    public void Dispose()
    {
        Stop();
        if (_stopped is { } stopped && !stopped.Finish(StopTimeout))
        {
            LogNotStoppedInTime(logger, StopTimeout.TotalSeconds);
        }

        _stopped = null;
    }

    private void Start()
    {
        var worker = system.StartWorker(() => LogCallCancelled(logger, CallLimit.TotalSeconds));
        Session? session = null;
        try
        {
            var applier = new WallpaperApplier(system, logger);
            IWallpaperWindow? window = null;
            window = system.CreateWindow(new WallpaperWindowEvents(
                ClockChanged: () => Guard(() =>
                {
                    system.TimeZone(readAgain: true);
                    Look();
                }),
                SettingChanged: () => Guard(Look),
                DisplaysChanged: () => Guard(Set),
                Resumed: () => Guard(Look),
                TaskbarCreated: () => Guard(() =>
                {
                    Post(applier.Reconnect);
                    Set();
                }),
                Unlocked: () => Guard(Set)));
            var timer = system.CreateTimer(() => window.Post(() => Guard(Look)));
            IDisposable? watch = null;
            try
            {
                watch = system.WatchBackground(() => window.Post(() => Guard(CheckOthers)));
                var pipe = system.ServePipe(request => AnswerAsync(applier, request), exception => LogPipeFailed(logger, exception.HResult));
                session = new Session(worker, applier, window, timer, watch, pipe);
            }
            catch
            {
                watch?.Dispose();
                timer.Dispose();
                window.Dispose();
                throw;
            }
        }
        catch
        {
            worker.Dispose();
            throw;
        }

        _session = session;
        LogStarted(logger);
    }

    private void Stop()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        _plan = null;
        session.Pipe.Dispose();
        session.Watch.Dispose();
        session.Timer.Dispose();
        session.Window.Dispose();
        session.Worker.Dispose();
        _stopped = session.Worker;
        LogStopped(logger);
    }

    // Turned off: puts back the background the user had, then stops. The host may also start with the
    // feature off and a background left to put back, as when the feature was turned off while
    // Explorer didn't answer, so the first time it looks with a wallpaper thread of its own.
    private void TurnOff()
    {
        if (_session is { } session)
        {
            Post(session.Applier.TurnOff);
            Stop();
            return;
        }

        if (_leftoverLooked)
        {
            return;
        }

        _leftoverLooked = true;
        var worker = system.StartWorker(() => LogCallCancelled(logger, CallLimit.TotalSeconds));
        var applier = new WallpaperApplier(system, logger);
        worker.Post(() =>
        {
            try
            {
                applier.TurnOff();
            }
            catch (Exception exception)
            {
                // An exception on the wallpaper thread would end the host.
                LogFailed(logger, exception);
            }
        });
        worker.Dispose();
        _stopped = worker;
    }

    // Has the wallpaper thread look at a change of the background, once for a burst of them.
    private void CheckOthers()
    {
        if (_session is not { } session || _plan is not { } plan || Interlocked.Exchange(ref _checkWaiting, 1) == 1)
        {
            return;
        }

        var settings = _settings;
        Post(() =>
        {
            Volatile.Write(ref _checkWaiting, 0);
            session.Applier.CheckOthers(settings, plan);
        });
    }

    // Works out the period now and when to look again, then has the wallpaper thread set it.
    private void Look()
    {
        if (_session is not { } session)
        {
            return;
        }

        var zone = system.TimeZone(readAgain: false);
        var place = WallpaperPlanner.PlaceOf(_settings, () => system.TimeZoneCity(zone));
        var dark = _settings.ChangeWith == ChangeWith.WindowsMode && system.IsWindowsModeDark();
        var plan = WallpaperPlanner.Plan(_settings, time.GetUtcNow(), zone, place, dark);
        if (plan.NextLook is { } next)
        {
            session.Timer.CallAt(next);
        }
        else
        {
            session.Timer.Cancel();
        }

        if (_plan?.Period != plan.Period)
        {
            LogPeriod(logger, plan.Period);
        }

        _plan = plan;
        Set();
    }

    // Has the wallpaper thread set the plan's pictures, where Windows shows others.
    private void Set()
    {
        if (_session is not { } session || _plan is not { } plan)
        {
            return;
        }

        var look = Interlocked.Increment(ref _look);
        var settings = _settings;
        Post(() =>
        {
            if (look == Volatile.Read(ref _look))
            {
                session.Applier.Apply(settings, plan);
            }
        });
    }

    // Runs a job on the wallpaper thread inside the feature's error boundary: a failure there keeps
    // the feature off until the host restarts.
    private void Post(Action job)
    {
        if (_session is not { } session)
        {
            return;
        }

        session.Worker.Post(() =>
        {
            try
            {
                job();
            }
            catch (Exception exception)
            {
                // An exception on the wallpaper thread would end the host.
                LogFailed(logger, exception);
                session.Window.Post(() =>
                {
                    _failed = true;
                    Stop();
                });
            }
        });
    }

    // Guards a handler on the host's UI thread, as the error boundary does Apply.
    private void Guard(Action handler)
    {
        try
        {
            handler();
        }
        catch (Exception exception)
        {
            // A handler that throws would end the host.
            LogFailed(logger, exception);
            _failed = true;
            Stop();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dynamic Wallpaper's pipe failed with 0x{Code:X8}.")]
    private static partial void LogPipeFailed(ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper changes with {ChangeWith}, with {Displays} displays of their own.")]
    private static partial void LogApplied(ILogger logger, ChangeWith changeWith, int displays);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dynamic Wallpaper's period is {Period}.")]
    private static partial void LogPeriod(ILogger logger, PeriodName period);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Explorer didn't answer within {Seconds} s, so Dynamic Wallpaper's call was cancelled.")]
    private static partial void LogCallCancelled(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Dynamic Wallpaper settings can't be read, so Dynamic Wallpaper is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Dynamic Wallpaper setting {Name} has a choice this version doesn't know, so it uses the default.")]
    private static partial void LogUnknownChoice(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dynamic Wallpaper failed, so it's off until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dynamic Wallpaper didn't stop within {Seconds} s.")]
    private static partial void LogNotStoppedInTime(ILogger logger, double seconds);

    private sealed record Session(IWallpaperWorker Worker, WallpaperApplier Applier, IWallpaperWindow Window, IMomentTimer Timer, IDisposable Watch, IDisposable Pipe);
}
