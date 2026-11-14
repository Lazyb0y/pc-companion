using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;

namespace PCCompanion.Modules.Input.LauncherActions;

/// <summary>
/// Runs PC Companion's actions from Start, Windows Search, other launchers and the command
/// <c>PCCompanion.exe --run</c>, which asks the host through a pipe while the feature is on. See
/// docs/features/input/launcher-actions.md.
/// </summary>
public sealed partial class LauncherActionsFeature : IFeature
{
    /// <summary>Lock's action ID.</summary>
    public const string LockAction = "lock";

    /// <summary>Turn off displays' action ID.</summary>
    public const string TurnOffDisplaysAction = "turnOffDisplays";

    /// <summary>Start the screen saver's action ID.</summary>
    public const string ScreenSaverAction = "screenSaver";

    // Turn off displays and Start the screen saver wait until nothing is held, looking this often,
    // for this long at most, and then a moment more, since any input would undo them.
    private static readonly TimeSpan ReleasePoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestHold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AfterRelease = TimeSpan.FromMilliseconds(500);

    private readonly IHostActions _actions;
    private readonly IHostNotifications _notifications;
    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger<LauncherActionsFeature> _logger;
    private readonly Func<ILauncherActionsSystem> _createSystem;
    private ILauncherActionsSystem? _system;
    private IDisposable? _pipe;
    private ReleaseWait? _wait;
    private bool _failed;

    /// <param name="actions">The other features' actions, which the host gives this feature.</param>
    public LauncherActionsFeature(IHostActions actions, IHostNotifications notifications, IHostCommands commands, TimeProvider time, ILogger<LauncherActionsFeature> logger)
        : this(actions, notifications, commands, time, logger, () => new WindowsLauncherActionsSystem())
    {
    }

    /// <param name="createSystem">Creates what the feature asks of Windows, on the host's UI thread, as it starts.</param>
    public LauncherActionsFeature(IHostActions actions, IHostNotifications notifications, IHostCommands commands, TimeProvider time, ILogger<LauncherActionsFeature> logger, Func<ILauncherActionsSystem> createSystem)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(createSystem);

        _actions = actions;
        _notifications = notifications;
        _commands = commands;
        _time = time;
        _logger = logger;
        _createSystem = createSystem;
    }

    // Why a request was refused, for the log.
    private enum Refusal
    {
        UnknownFeature,
        FeatureOff,
        RefusedByFeature,
        UnknownAction,
    }

    public static FeatureInfo Definition { get; } = new("launcherActions", InputModule.Id, "LauncherActionsName", "LauncherActionsDescription", "");

    /// <summary>Whether this PC can run Launcher Actions, for the settings app, which doesn't create the feature.</summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    /// <summary>Whether the feature serves its pipe.</summary>
    public bool IsRunning => _pipe is not null;

    /// <summary>
    /// The actions of its own the feature offers to launchers and the command, named in the user's
    /// language: Lock, Turn off displays and Start the screen saver.
    /// </summary>
    public static IReadOnlyList<OfferedAction> OwnActions() =>
    [
        new(new FeatureAction(Definition.Id, LockAction), Strings.LauncherActionsActionLock),
        new(new FeatureAction(Definition.Id, TurnOffDisplaysAction), Strings.LauncherActionsActionTurnOffDisplays),
        new(new FeatureAction(Definition.Id, ScreenSaverAction), Strings.LauncherActionsActionScreenSaver),
    ];

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        LauncherActionsSettings read;
        try
        {
            read = LauncherActionsSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            read = new LauncherActionsSettings();
        }

        if (!read.Enabled)
        {
            Stop();
        }
        else if (_system is null)
        {
            Start();
        }
    }

    public void Dispose() => Stop();

    private void Start()
    {
        var system = _createSystem();
        try
        {
            _pipe = system.Serve(request => AnswerAsync(system, request), exception => system.Post(() => PipeFailed(exception)));
        }
        catch
        {
            system.Dispose();
            throw;
        }

        _system = system;
        LogStarted(_logger);
    }

    // Stops serving first, so a request that waits for the UI thread gets no answer rather than a wrong one.
    private void Stop()
    {
        if (_system is null)
        {
            return;
        }

        _pipe?.Dispose();
        _pipe = null;
        _wait?.Dispose();
        _wait = null;
        _system.Dispose();
        _system = null;
        LogStopped(_logger);
    }

    // On the thread pool. The request runs on the host's UI thread, after the ones before it.
    private Task<byte[]> AnswerAsync(ILauncherActionsSystem system, byte[] request)
    {
        if (LauncherPipe.ReadRequest(request) is not { } action)
        {
            LogInvalidRequest(_logger);
            return Task.FromResult(LauncherPipe.WriteAnswer(LauncherResult.Invalid));
        }

        // Unanswered while the feature stops, which ends the pipe's wait for it.
        var answer = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        system.Post(() =>
        {
            if (system != _system)
            {
                return;
            }

            try
            {
                answer.TrySetResult(LauncherPipe.WriteAnswer(Handle(action)));
            }
            catch (Exception exception)
            {
                LogFailed(_logger, exception);
                _failed = true;
                Stop();
            }
        });

        return answer.Task;
    }

    // On the host's UI thread.
    private LauncherResult Handle(FeatureAction action)
    {
        if (action.FeatureId == Definition.Id)
        {
            return RunOwn(action);
        }

        if (_actions.CanRunFromLauncher(action))
        {
            _actions.Run(action);
            LogRan(_logger, action.FeatureId, action.ActionId);
            return LauncherResult.Ran;
        }

        var feature = _actions.FindFeature(action.FeatureId);
        if (feature is null)
        {
            RefuseUnknown(action, Refusal.UnknownFeature);
        }
        else if (!_actions.IsRunning(feature.Id))
        {
            var name = NameOf(feature);
            Refuse(action, Refusal.FeatureOff, name, Format(Strings.LauncherActionsNotificationOff, name), feature.Id);
        }
        else
        {
            var name = NameOf(feature);
            Refuse(action, Refusal.RefusedByFeature, name, Format(Strings.LauncherActionsNotificationRefused, name), feature.Id);
        }

        return LauncherResult.Refused;
    }

    private LauncherResult RunOwn(FeatureAction action)
    {
        var system = _system!;
        Action? run = action.Target is not null ? null : action.ActionId switch
        {
            LockAction => system.Lock,
            TurnOffDisplaysAction => () => AfterReleased(system, system.TurnOffDisplays),
            ScreenSaverAction => () => AfterReleased(system, system.StartScreenSaver),
            _ => null,
        };

        if (run is null)
        {
            RefuseUnknown(action, Refusal.UnknownAction);
            return LauncherResult.Refused;
        }

        run();
        LogRan(_logger, action.FeatureId, action.ActionId);
        return LauncherResult.Ran;
    }

    // Selecting the notification opens the page where the action can be checked.
    private void Refuse(FeatureAction action, Refusal refusal, string title, string text, string pageId)
    {
        LogRefused(_logger, action.FeatureId, action.ActionId, refusal);
        try
        {
            _notifications.Show(title, text, () => _commands.OpenFeatureSettings(pageId));
        }
        catch (Exception exception)
        {
            LogNotificationFailed(_logger, exception);
        }
    }

    private void RefuseUnknown(FeatureAction action, Refusal refusal) =>
        Refuse(action, refusal, Strings.LauncherActionsName, Format(Strings.LauncherActionsNotificationUnknown, Strings.LauncherActionsName), Definition.Id);

    private static string NameOf(FeatureInfo feature) =>
        Strings.ResourceManager.GetString(feature.NameKey, CultureInfo.CurrentUICulture) ?? feature.Id;

    private static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);

    // Looks every ReleasePoll until nothing is held, or LongestHold passed, then waits AfterRelease.
    // A later wait replaces an earlier one.
    private void AfterReleased(ILauncherActionsSystem system, Action action)
    {
        _wait?.Dispose();
        var wait = new ReleaseWait(action, _time.GetTimestamp());
        _wait = wait;
        wait.Timer = _time.CreateTimer(_ => system.Post(() => Look(system, wait)), null, ReleasePoll, ReleasePoll);
    }

    private void Look(ILauncherActionsSystem system, ReleaseWait wait)
    {
        if (_wait != wait || system != _system)
        {
            return;
        }

        if (!wait.Released)
        {
            if (system.IsInputHeld() && _time.GetElapsedTime(wait.Started) < LongestHold)
            {
                return;
            }

            wait.Released = true;
            wait.Timer?.Change(AfterRelease, Timeout.InfiniteTimeSpan);
            return;
        }

        _wait = null;
        wait.Dispose();
        try
        {
            wait.Action();
        }
        catch (Exception exception)
        {
            LogFailed(_logger, exception);
            _failed = true;
            Stop();
        }
    }

    // On the host's UI thread, after the pipe failed on the thread pool.
    private void PipeFailed(Exception exception)
    {
        if (_system is null)
        {
            return;
        }

        LogPipeFailed(_logger, exception.HResult);
        _failed = true;
        Stop();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Launcher Actions settings can't be read, so Launcher Actions is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Launcher Actions takes requests.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Launcher Actions stopped taking requests.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature's action {ActionId} ran from a launcher or the command.")]
    private static partial void LogRan(ILogger logger, string featureId, string actionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The {FeatureId} feature's action {ActionId} was asked for from a launcher or the command and refused: {Refusal}.")]
    private static partial void LogRefused(ILogger logger, string featureId, string actionId, Refusal refusal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A request through Launcher Actions' pipe isn't one, so nothing ran.")]
    private static partial void LogInvalidRequest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Launcher Actions' pipe stopped working (0x{HResult:X8}), so Launcher Actions is off until PC Companion restarts.")]
    private static partial void LogPipeFailed(ILogger logger, int hResult);

    [LoggerMessage(Level = LogLevel.Error, Message = "Launcher Actions failed, so it's off until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Launcher Actions' notification couldn't be shown.")]
    private static partial void LogNotificationFailed(ILogger logger, Exception exception);

    // Turn off displays or Start the screen saver, waiting until nothing is held.
    private sealed class ReleaseWait(Action action, long started) : IDisposable
    {
        public Action Action => action;

        public long Started => started;

        public ITimer? Timer { get; set; }

        public bool Released { get; set; }

        public void Dispose() => Timer?.Dispose();
    }
}
