using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>
/// Switches the displays between named profiles from the PC Companion icon's menu and the
/// settings app's page, and asks whether to keep a switch that could leave a display black. See
/// docs/features/display/display-profiles.md.
/// </summary>
/// <param name="pipeName">The pipe through which the settings app asks for a switch; <see cref="DisplayProfilesPipe.Name"/>.</param>
public sealed partial class DisplayProfilesFeature(IDisplayProfilesSystem system, string pipeName, IHostCommands commands, ILogger<DisplayProfilesFeature> logger)
    : IFeature, IHostMenuItems
{
    /// <summary>The time the question counts down from before the switch is reverted.</summary>
    public static readonly TimeSpan QuestionTime = TimeSpan.FromSeconds(15);

    // How long stopping waits for a switch that waits for an answer to be reverted.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    // How long the settings app's request waits for the switch, which takes well under a second.
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _readGate = new();
    private ReadState _read;

    // Set and cleared on the host's UI thread; the thread pool reads it to post its results.
    private volatile Session? _session;

    // On the host's UI thread.
    private IReadOnlyList<NumberedProfile> _profiles = [];

    // The profiles of the menu that opened last, as it numbered them. The settings can change while
    // it's open, which moves or changes the profiles.
    private IReadOnlyList<MenuProfile> _menuProfiles = [];
    private CurrentSetup? _current;
    private bool _remote;
    private int? _inUse;
    private bool _failed;

    public DisplayProfilesFeature(IHostCommands commands, ILogger<DisplayProfilesFeature> logger)
        : this(new WindowsDisplayProfilesSystem(), DisplayProfilesPipe.Name, commands, logger)
    {
    }

    private enum ReadState
    {
        Idle,
        Queued,
        Running,

        // Running, and asked again since it started.
        RunningAgain,
    }

    public static FeatureInfo Definition { get; } = new("displayProfiles", DisplayModule.Id, "DisplayProfilesName", "DisplayProfilesDescription", "");

    /// <summary>
    /// Whether this PC can run Display Profiles, for the settings app, which doesn't create the feature.
    /// </summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    public bool IsRunning => _session is not null;

    public void Apply(JsonElement? settings)
    {
        if (_failed)
        {
            return;
        }

        DisplayProfilesSettings read;
        try
        {
            read = DisplayProfilesSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(logger, exception);
            Stop();
            return;
        }

        if (!read.Enabled)
        {
            Stop();
            return;
        }

        _profiles = Usable(read.Profiles);
        if (_session is null)
        {
            Start();
        }

        UpdateInUse();
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems()
    {
        if (_session is null)
        {
            _menuProfiles = [];
            return [];
        }

        _menuProfiles = [.. _profiles.Select(profile => new MenuProfile(profile.Number, profile.Profile, CanBeUsed(profile.Profile)))];
        return DisplayProfilesMenu.Build(_menuProfiles, _inUse, _current is not null, _remote);
    }

    public void ChooseMenuItem(int id)
    {
        if (_session is not { } session)
        {
            return;
        }

        if (id == DisplayProfilesMenu.SettingsItem)
        {
            commands.OpenFeatureSettings(Definition.Id);
            return;
        }

        // The profile the menu showed, wherever it is now, unless it was changed or deleted since.
        if (_menuProfiles.FirstOrDefault(profile => profile.Number == id) is not { } shown
            || _profiles.FirstOrDefault(profile => profile.Profile == shown.Profile) is not { } chosen)
        {
            LogNoSuchProfile(logger, id);
            return;
        }

        if (_remote)
        {
            LogRemoteChoice(logger, chosen.Number);
            return;
        }

        LogChosen(logger, chosen.Number);
        RunInBackground(session, () => Switch(session, chosen.Number, chosen.Profile, answer: null));
    }

    public void Dispose() => Stop();

    // The profiles the menu shows, up to the most it shows; each other one is logged with its number.
    private List<NumberedProfile> Usable(IReadOnlyList<DisplayProfile> profiles)
    {
        var usable = new List<NumberedProfile>();
        for (var index = 0; index < profiles.Count; index++)
        {
            var number = index + 1;
            if (number > DisplayProfilesSettings.MaximumProfiles)
            {
                LogTooManyProfiles(logger, profiles.Count, DisplayProfilesSettings.MaximumProfiles);
                break;
            }

            var problem = DisplayProfileCheck.Check(profiles[index], usable.Select(other => other.Profile.Name));
            if (problem != DisplayProfileProblem.None)
            {
                LogUnusableProfile(logger, number, problem);
                continue;
            }

            usable.Add(new NumberedProfile(number, profiles[index]));
        }

        return usable;
    }

    // Before the displays are read, a profile counts as usable; choosing it then finds out.
    private bool CanBeUsed(DisplayProfile profile) =>
        _current is not { } current || ProfileRecognition.Recognize(profile, current).CanBeUsed;

    private void Start()
    {
        var session = new Session(new DisplaySwitcher(system));
        session.Window = system.CreateWindow(
            () => Guard(session, RequestRead),
            () => Guard(session, () => CantAnswer(session)));
        try
        {
            session.Pipe = RequestPipe.Serve(
                pipeName,
                DisplayProfilesPipe.MaximumRequestLength,
                request => AnswerAsync(session, request),
                exception => LogPipeFailed(logger, exception.HResult));
        }
        catch
        {
            session.Window.Dispose();
            throw;
        }

        _session = session;
        LogStarted(logger);
        RequestRead();
    }

    // Reverts a switch that waits for an answer, after a switch that's running, since the host may
    // exit next; then lets go of the window and its notifications.
    private void Stop()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        session.Pipe?.Dispose();
        CloseQuestion(session);
        var finished = system.RunInBackgroundAndWait(
            () =>
            {
                try
                {
                    if (session.Switcher.Interrupt() is { } outcome)
                    {
                        LogEnded(logger, outcome);
                    }
                }
                catch (Win32Exception exception)
                {
                    LogCantRevert(logger, exception.NativeErrorCode);
                }
                catch (Exception exception)
                {
                    LogFailed(logger, exception);
                }
            },
            StopTimeout);
        if (!finished)
        {
            LogStopTimedOut(logger);
        }

        session.Window?.Dispose();
        (_current, _remote, _inUse) = (null, false, null);
        LogStopped(logger);
    }

    // From the host's UI thread: reads the displays, or once more after the read that's running.
    private void RequestRead()
    {
        if (_session is not { } session)
        {
            return;
        }

        lock (_readGate)
        {
            switch (_read)
            {
                case ReadState.Idle:
                    _read = ReadState.Queued;
                    break;
                case ReadState.Running:
                    _read = ReadState.RunningAgain;
                    return;
                default:
                    return;
            }
        }

        system.RunInBackground(() => Read(session));
    }

    // On the thread pool.
    private void Read(Session session)
    {
        lock (_readGate)
        {
            _read = ReadState.Running;
        }

        if (_session == session)
        {
            try
            {
                var remote = system.IsRemoteSession;
                var current = remote ? null : system.Read();
                session.Window!.Post(() => Guard(session, () => Update(current, remote)));
            }
            catch (Win32Exception exception)
            {
                // The menu keeps what it shows, and a switch reads the displays again first.
                var error = exception.NativeErrorCode;
                session.Window!.Post(() => Guard(session, () => LogCantRead(logger, error)));
            }
            catch (Exception exception)
            {
                session.Window!.Post(() => Fail(session, exception));
            }
        }

        bool again;
        lock (_readGate)
        {
            again = _read == ReadState.RunningAgain;
            _read = again ? ReadState.Queued : ReadState.Idle;
        }

        if (again)
        {
            system.RunInBackground(() => Read(session));
        }
    }

    // On the thread pool: a request from the settings app, answered once the switch has run.
    private async Task<byte[]> AnswerAsync(Session session, byte[] request)
    {
        if (DisplayProfilesPipe.ReadRequest(request) is not { } read)
        {
            return DisplayProfilesPipe.Write(new DisplayProfilesAnswer { Status = DisplayProfilesAnswerStatus.UnknownProfile });
        }

        var answer = new TaskCompletionSource<DisplayProfilesAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Window!.Post(() => Guard(session, () => Requested(session, read, answer)));
        try
        {
            return DisplayProfilesPipe.Write(await answer.Task.WaitAsync(AnswerTimeout).ConfigureAwait(false));
        }
        catch (TimeoutException)
        {
            // Turned off meanwhile, so nothing more runs for this session.
            return DisplayProfilesPipe.Write(new DisplayProfilesAnswer { Status = DisplayProfilesAnswerStatus.Off });
        }
    }

    // A switch asked for on the page: only to a profile the host has unchanged, as with the menu.
    private void Requested(Session session, DisplayProfilesRequest request, TaskCompletionSource<DisplayProfilesAnswer> answer)
    {
        if (_profiles.FirstOrDefault(profile => profile.Number == request.Number && profile.Profile == request.Profile) is not { } chosen)
        {
            LogNoSuchProfile(logger, request.Number);
            answer.TrySetResult(new DisplayProfilesAnswer { Status = DisplayProfilesAnswerStatus.UnknownProfile });
            return;
        }

        if (_remote)
        {
            LogRemoteChoice(logger, chosen.Number);
            answer.TrySetResult(new DisplayProfilesAnswer { Status = DisplayProfilesAnswerStatus.RemoteSession });
            return;
        }

        LogChosenOnPage(logger, chosen.Number);
        RunInBackground(session, () => Switch(session, chosen.Number, chosen.Profile, answer));
    }

    // On the thread pool, after the reads and switches queued before it.
    private void Switch(Session session, int number, DisplayProfile profile, TaskCompletionSource<DisplayProfilesAnswer>? answer)
    {
        SwitchResult result;
        try
        {
            result = session.Switcher.Switch(profile);
        }
        catch (Win32Exception exception)
        {
            var error = exception.NativeErrorCode;
            answer?.TrySetResult(new DisplayProfilesAnswer { Status = DisplayProfilesAnswerStatus.Failed, ErrorCode = error });
            session.Window!.Post(() => Guard(session, () => LogCantRead(logger, error)));
            return;
        }

        answer?.TrySetResult(new DisplayProfilesAnswer
        {
            Status = result.Outcome switch
            {
                SwitchOutcome.AlreadyInUse => DisplayProfilesAnswerStatus.AlreadyInUse,
                SwitchOutcome.NotConnected => DisplayProfilesAnswerStatus.NotConnected,
                SwitchOutcome.Refused => DisplayProfilesAnswerStatus.Refused,
                SwitchOutcome.Kept => DisplayProfilesAnswerStatus.Kept,
                _ => DisplayProfilesAnswerStatus.Asking,
            },
            ErrorCode = result.Error ?? 0,
            ModesNotOffered = result.ModesNotOffered ?? [],
            NotAsInProfile = result.NotAsInProfile ?? [],
        });
        session.Window!.Post(() => Guard(session, () => Switched(session, number, result)));
    }

    private void Update(CurrentSetup? current, bool remote)
    {
        if (remote != _remote)
        {
            LogRemote(logger, remote);
            _remote = remote;
        }

        if (current is not null)
        {
            LogDisplayChanges(_current, current);
        }

        _current = current;
        UpdateInUse();
    }

    private void Switched(Session session, int number, SwitchResult result)
    {
        switch (result.Outcome)
        {
            case SwitchOutcome.AlreadyInUse:
                LogAlreadyInUse(logger, number);
                break;
            case SwitchOutcome.NotConnected:
                LogNotConnected(logger, number);
                break;
            case SwitchOutcome.Refused:
                LogRefused(logger, number, result.Error ?? 0, Places(result.ModesNotOffered));
                break;
            case SwitchOutcome.Kept:
                CloseQuestion(session);
                LogKeptAtOnce(logger, number);
                break;
            case SwitchOutcome.Asking:
                CloseQuestion(session);
                LogAsking(logger, number);
                Ask(session, result.Question);
                break;
        }

        if (result.NotAsInProfile is { Count: > 0 } notAsInProfile)
        {
            LogNotAsInProfile(logger, number, Places(notAsInProfile));
        }

        if (result.HdrError is { } hdrError)
        {
            LogHdrRefused(logger, number, hdrError);
        }

        RequestRead();
    }

    private void Ask(Session session, int question)
    {
        session.Question = question;
        session.Dialog = system.Ask(
            QuestionTime,
            kept => session.Window!.Post(() => Guard(session, () => Answered(session, question, kept))),
            exception => session.Window!.Post(() => Fail(session, exception)));
        session.Timer = session.Window!.StartTimer(QuestionTime, () => Guard(session, () => TimeUp(session, question)));
    }

    private void Answered(Session session, int question, bool kept)
    {
        // A question this feature closed has already been dealt with.
        if (session.Question != question)
        {
            return;
        }

        CloseQuestion(session);
        LogAnswered(logger, kept);
        Finish(session, () => session.Switcher.Answer(question, kept));
    }

    private void TimeUp(Session session, int question)
    {
        if (session.Question != question)
        {
            return;
        }

        CloseQuestion(session);
        LogTimeUp(logger);
        Finish(session, () => session.Switcher.Answer(question, kept: false));
    }

    // The session locks or switches to another user, or the PC goes to sleep.
    private void CantAnswer(Session session)
    {
        if (session.Question == 0)
        {
            return;
        }

        CloseQuestion(session);
        LogCantAnswer(logger);
        Finish(session, session.Switcher.Interrupt);
    }

    // Keeps or reverts on the thread pool, after the switch it's about.
    private void Finish(Session session, Func<AnswerOutcome?> finish) => RunInBackground(session, () =>
    {
        AnswerOutcome? outcome;
        try
        {
            outcome = finish();
        }
        catch (Win32Exception exception)
        {
            var error = exception.NativeErrorCode;
            session.Window!.Post(() => Guard(session, () => LogCantRevert(logger, error)));
            return;
        }

        session.Window!.Post(() => Guard(session, () =>
        {
            if (outcome is { } ended)
            {
                LogEnded(logger, ended);
            }

            RequestRead();
        }));
    });

    // Forgets the question first, so the answer that closing it gives is dropped.
    private static void CloseQuestion(Session session)
    {
        var (timer, dialog) = (session.Timer, session.Dialog);
        (session.Timer, session.Dialog, session.Question) = (null, null, 0);
        timer?.Dispose();
        dialog?.Close();
    }

    private void UpdateInUse()
    {
        if (_current is not { } current)
        {
            _inUse = null;
            return;
        }

        var inUse = _profiles.Where(profile => ProfileRecognition.Matches(profile.Profile, current)).Select(profile => (int?)profile.Number).FirstOrDefault();
        if (inUse != _inUse)
        {
            if (inUse is { } number)
            {
                LogInUse(logger, number);
            }
            else
            {
                LogNoneInUse(logger);
            }
        }

        _inUse = inUse;
    }

    // Displays by model and connection type only: no names or serial numbers.
    private void LogDisplayChanges(CurrentSetup? before, CurrentSetup after)
    {
        if (before is null)
        {
            return;
        }

        var paths = before.Setup.Displays.Select(display => display.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var display in after.Setup.Displays.Where(display => !paths.Remove(display.DevicePath)))
        {
            LogDisplayConnected(logger, display.Model, display.Connection);
        }

        foreach (var display in before.Setup.Displays.Where(display => paths.Contains(display.DevicePath)))
        {
            LogDisplayRemoved(logger, display.Model, display.Connection);
        }
    }

    // A profile's displays by their places in it, from 1.
    private static string Places(IReadOnlyList<int>? places) => places is null ? "" : string.Join(", ", places.Select(place => place + 1));

    // Runs work on the thread pool for a session that's still running; a failure turns it off.
    private void RunInBackground(Session session, Action action) => system.RunInBackground(() =>
    {
        if (_session != session)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            session.Window!.Post(() => Fail(session, exception));
        }
    });

    // Runs a result on the host's UI thread, where an exception would end the host, and only for
    // the session it belongs to: results that come after the feature stopped are dropped.
    private void Guard(Session session, Action action)
    {
        if (_session != session)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Fail(session, exception);
        }
    }

    // Like a failing Apply, a failure keeps the feature off until the host restarts; stopping
    // reverts a switch that waits for an answer.
    private void Fail(Session session, Exception exception)
    {
        if (_session != session)
        {
            return;
        }

        LogFailed(logger, exception);
        _failed = true;
        Stop();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Display Profiles settings can't be read, so Display Profiles is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display profile {Number} can't be used ({Problem}), so it's left out of the menu.")]
    private static partial void LogUnusableProfile(ILogger logger, int number, DisplayProfileProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "There are {Count} display profiles; only the first {Maximum} are used.")]
    private static partial void LogTooManyProfiles(ILogger logger, int count, int maximum);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display Profiles started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display Profiles stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remote session: {Remote}. Display profiles can't be used in a remote session.")]
    private static partial void LogRemote(ILogger logger, bool remote);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A display was connected: {Model} on {Connection}.")]
    private static partial void LogDisplayConnected(ILogger logger, string model, DisplayConnection connection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A display was disconnected: {Model} on {Connection}.")]
    private static partial void LogDisplayRemoved(ILogger logger, string model, DisplayConnection connection);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The display setup matches display profile {Number}.")]
    private static partial void LogInUse(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The display setup matches none of the display profiles.")]
    private static partial void LogNoneInUse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to read the displays (error {Error}).")]
    private static partial void LogCantRead(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display profile {Number} was chosen.")]
    private static partial void LogChosen(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display profile {Number} was chosen on the page.")]
    private static partial void LogChosenOnPage(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings app's pipe failed (HRESULT {HResult}).")]
    private static partial void LogPipeFailed(ILogger logger, int hResult);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display profile {Number} was chosen, but there's no such profile any more.")]
    private static partial void LogNoSuchProfile(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display profile {Number} was chosen in a remote session, where it can't be used.")]
    private static partial void LogRemoteChoice(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display profile {Number} is already in use.")]
    private static partial void LogAlreadyInUse(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display profile {Number} can't be used: a display that's on in it isn't connected.")]
    private static partial void LogNotConnected(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused display profile {Number} (error {Error}); displays whose mode Windows doesn't list: {Displays}.")]
    private static partial void LogRefused(ILogger logger, int number, int error, string displays);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switched to display profile {Number} and kept it, since it only moves displays.")]
    private static partial void LogKeptAtOnce(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switched to display profile {Number}; asking whether to keep it.")]
    private static partial void LogAsking(ILogger logger, int number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows applied display profile {Number} in its own way on its displays {Displays}.")]
    private static partial void LogNotAsInProfile(ILogger logger, int number, string displays);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to turn HDR on or off for display profile {Number} (error {Error}).")]
    private static partial void LogHdrRefused(ILogger logger, int number, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The question was answered: keep {Kept}.")]
    private static partial void LogAnswered(ILogger logger, bool kept);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The question wasn't answered in time, so the switch is reverted.")]
    private static partial void LogTimeUp(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The user can't answer the question, so the switch is reverted.")]
    private static partial void LogCantAnswer(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The switch ended: {Outcome}.")]
    private static partial void LogEnded(ILogger logger, AnswerOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to keep or revert the switch (error {Error}).")]
    private static partial void LogCantRevert(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping Display Profiles didn't finish reverting a switch in time.")]
    private static partial void LogStopTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Display Profiles failed, so it's off until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    // A profile that passed the checks, with its number in the profile list, from 1.
    private sealed record NumberedProfile(int Number, DisplayProfile Profile);

    // What a start of the feature has: results from another start are dropped.
    private sealed class Session(DisplaySwitcher switcher)
    {
        // On the thread pool, one action at a time.
        public DisplaySwitcher Switcher { get; } = switcher;

        public IDisplayWindow? Window { get; set; }

        public RequestPipe? Pipe { get; set; }

        // The question that's open, on the host's UI thread; 0 when none is.
        public int Question { get; set; }

        public IKeepQuestion? Dialog { get; set; }

        public IDisposable? Timer { get; set; }
    }
}
