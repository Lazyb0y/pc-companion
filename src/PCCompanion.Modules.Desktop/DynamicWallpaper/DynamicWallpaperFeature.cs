using System;
using System.Text.Json;
using System.Threading;
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

    // How long the host waits as it exits for the wallpaper thread to finish its job.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private Session? _session;
    private IWallpaperWorker? _stopped;
    private DynamicWallpaperSettings _settings = new();
    private WallpaperPlan? _plan;
    private bool _failed;

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
            Stop();
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
            session = new Session(worker, applier, window, timer);
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
        session.Timer.Dispose();
        session.Window.Dispose();
        session.Worker.Dispose();
        _stopped = session.Worker;
        LogStopped(logger);
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
            // An exception on the wallpaper thread would end the host.
            catch (Exception exception)
            {
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
        // A handler that throws would end the host.
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            _failed = true;
            Stop();
        }
    }

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

    private sealed record Session(IWallpaperWorker Worker, WallpaperApplier Applier, IWallpaperWindow Window, IMomentTimer Timer);
}
