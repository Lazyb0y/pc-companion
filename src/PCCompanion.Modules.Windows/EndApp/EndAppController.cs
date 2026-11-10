using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.EndApp;

/// <summary>
/// End App on the feature's thread: choosing an app with the pointer or the keys, the
/// confirmation that follows the app's state, and ending the app and its helpers on the thread
/// pool. See docs/features/windows/end-app.md. Everything runs on the feature's thread; timers
/// and work on the thread pool come back through <c>post</c>.
/// </summary>
public sealed partial class EndAppController : IDisposable
{
    /// <summary>How long choosing waits for a pointer move or a key before it stops.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(30);

    /// <summary>How often the open question looks at the app's state.</summary>
    public static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long ending waits for each process to exit.</summary>
    public static readonly TimeSpan EndWait = TimeSpan.FromSeconds(5);

    // The frame's width in effective pixels.
    private const int FrameWidth = 4;

    private const uint KeyTab = 0x09;
    private const uint KeyEnter = 0x0D;
    private const uint KeyEscape = 0x1B;
    private const uint KeySpace = 0x20;

    private const int EndButton = 0;
    private const int OpenTaskManagerButton = 0;

    private readonly IEndAppSystem _system;
    private readonly IEndAppView _view;
    private readonly IEndAppHost _host;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger _logger;
    private readonly AppChooser _chooser;

    // Each time choosing starts, so that results that come late from the thread pool are dropped.
    private int _session;
    private IReadOnlyList<EndAppWindow> _windows = [];
    private nint _pointed = -1;

    // Where the pointer was last. Windows also reports a move when windows change under a pointer
    // that rests, as when the covering window appears or the frame moves for Tab, and such a move
    // mustn't take the frame from the window in front or the one Tab reached.
    private Point? _pointer;
    private ChosenApp _framed = ChosenApp.Nothing;
    private readonly Dictionary<uint, string> _names = [];
    private readonly HashSet<uint> _nameRequests = [];
    private ITimer? _idle;
    private ITimer? _watch;

    // The app the question is about.
    private ChosenApp _asked = ChosenApp.Nothing;
    private IEndAppProcess? _held;
    private IEndAppQuestion? _question;
    private bool _askedToEnd;
    private bool _notResponding;
    private string _name = string.Empty;
    private string _program = string.Empty;
    private int _processes;

    /// <param name="post">Runs an action on the feature's thread, from any thread.</param>
    public EndAppController(IEndAppSystem system, IEndAppView view, IEndAppHost host, TimeProvider time, Action<Action> post, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _view = view;
        _host = host;
        _time = time;
        _post = post;
        _logger = logger;
        _chooser = new AppChooser(system);
    }

    public EndAppStage Stage { get; private set; }

    /// <summary>The app the frame shows while the user chooses.</summary>
    public ChosenApp Framed => _framed;

    /// <summary>
    /// Starts choosing an app, for the shortcut or the PC Companion icon's menu. The shortcut
    /// pressed again while choosing stops it.
    /// </summary>
    public void Start(EndAppSource source)
    {
        if (Stage == EndAppStage.Choosing)
        {
            Stop(StopReason.Shortcut);
            return;
        }

        if (Stage != EndAppStage.Idle)
        {
            LogBusy(_logger, Stage);
            return;
        }

        _session++;
        _chooser.Forget();
        _names.Clear();
        _nameRequests.Clear();
        _pointed = -1;

        var front = source == EndAppSource.Shortcut ? _system.ForegroundWindow : 0;
        _pointer = _system.PointerPosition;
        if (!_view.ShowCovering())
        {
            _view.HideCovering();
            LogNotInFront(_logger);
            return;
        }

        Stage = EndAppStage.Choosing;
        _windows = _system.ListWindows();
        var first = FrontWindow(front);
        var order = _chooser.TabOrder(_windows);
        Frame(first != 0 ? first : order.Count > 0 ? order[0] : 0);
        var session = _session;
        _idle = _time.CreateTimer(_ => _post(() => OnIdle(session)), null, IdleLimit, Timeout.InfiniteTimeSpan);
        LogStarted(_logger, source);
    }

    public void OnPointerMoved(Point point)
    {
        if (Stage != EndAppStage.Choosing || point == _pointer)
        {
            return;
        }

        _pointer = point;
        StillActive();
        _windows = _system.ListWindows();
        var window = _chooser.WindowAt(point, _windows);
        if (window != _pointed)
        {
            _pointed = window;
            Frame(window);
        }
    }

    public void OnClicked(Point point, bool right)
    {
        if (Stage != EndAppStage.Choosing)
        {
            return;
        }

        if (right)
        {
            Stop(StopReason.RightClick);
            return;
        }

        _windows = _system.ListWindows();
        _pointed = _chooser.WindowAt(point, _windows);
        Frame(_pointed);
        Choose();
    }

    public void OnKey(uint key, bool shift)
    {
        if (Stage != EndAppStage.Choosing)
        {
            return;
        }

        StillActive();
        switch (key)
        {
            case KeyEscape:
                Stop(StopReason.Escape);
                break;
            case KeyEnter:
            case KeySpace:
                Choose();
                break;
            case KeyTab:
                MoveByTab(shift);
                break;
        }
    }

    /// <summary>The covering window lost the front: choosing stops, or, once the question took it, the covering window goes.</summary>
    public void OnDeactivated()
    {
        if (Stage == EndAppStage.Choosing)
        {
            Stop(StopReason.LostFront);
        }
        else if (Stage == EndAppStage.Asking)
        {
            HideChoosing();
        }
    }

    public void OnDisplaysChanged()
    {
        if (Stage == EndAppStage.Choosing)
        {
            Stop(StopReason.DisplaysChanged);
        }
    }

    public void Dispose() => Finish();

    private nint FrontWindow(nint front)
    {
        if (front == 0)
        {
            return 0;
        }

        var root = _system.RootOwner(front);
        return _windows.Any(window => window.Handle == root && window.ProcessId != _system.OwnProcessId && !window.IsCloaked) ? root : 0;
    }

    private void MoveByTab(bool back)
    {
        var order = _chooser.TabOrder(_windows);
        if (order.Count == 0)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < order.Count && index < 0; i++)
        {
            index = order[i] == _framed.Window ? i : -1;
        }

        var next = index < 0 ? (back ? order.Count - 1 : 0) : (index + (back ? order.Count - 1 : 1)) % order.Count;
        _pointed = order[next];
        Frame(order[next]);
    }

    private void Frame(nint window)
    {
        _framed = _chooser.Describe(window);
        var bounds = BoundsOf(_framed.Window) ?? BoundsOf(window);
        if (_framed.Window == 0 || bounds is null)
        {
            _view.HideFrame();
        }
        else
        {
            var thickness = Math.Max(1, (int)Math.Round(FrameWidth * _system.DpiOf(_framed.Window) / 96.0));
            _view.ShowFrame(bounds.Value, thickness, _framed.CanEnd);
        }

        _view.SetPointer(_framed.CanEnd);
        RequestName(_framed);
        UpdateTitle();
    }

    private Rectangle? BoundsOf(nint window) =>
        window == 0 ? null : _windows.FirstOrDefault(candidate => candidate.Handle == window)?.VisibleBounds;

    private void RequestName(ChosenApp app)
    {
        if (app.ProcessId == 0 || _names.ContainsKey(app.ProcessId) || !_nameRequests.Add(app.ProcessId))
        {
            return;
        }

        var session = _session;
        _host.Background(() =>
        {
            var name = _system.AppNameAndIcon(app.AppWindow, app.ProcessId).Name;
            _post(() =>
            {
                if (session == _session && Stage == EndAppStage.Choosing)
                {
                    _names[app.ProcessId] = name;
                    UpdateTitle();
                }
            });
        });
    }

    private void UpdateTitle()
    {
        var title = Strings.EndAppChoosing;
        if (_framed.ProcessId != 0 && _names.TryGetValue(_framed.ProcessId, out var name))
        {
            var state = !_framed.CanEnd ? Strings.EndAppFramedCantEnd
                : _system.IsNotResponding(_framed.AppWindow) ? Strings.EndAppFramedNotResponding
                : Strings.EndAppFramedResponding;
            title += " " + Format(state, name);
        }

        _view.SetTitle(title);
    }

    private void Choose()
    {
        var app = _framed;
        if (app.Window == 0)
        {
            return;
        }

        StopIdle();
        Stage = EndAppStage.Reading;
        var refusal = app.Refusal;
        if (refusal == EndRefusal.None)
        {
            // Held from now until it ends, so that Windows can't give its ID to another process.
            _held = _system.Open(app.ProcessId, out _);
            if (_held is null)
            {
                refusal = _system.IsElevated(app.ProcessId) == true ? EndRefusal.RunsAsAdministrator : EndRefusal.NotAllowed;
            }
        }

        var held = _held;
        var session = _session;
        _host.Background(() =>
        {
            var (name, icon) = app.ProcessId == 0 ? (string.Empty, null) : _system.AppNameAndIcon(app.AppWindow, app.ProcessId);
            var helpers = held is null ? 0 : AppChooser.Helpers(_system, held.Id, held.StartTime, held.ProgramPath).Count;
            _post(() =>
            {
                if (session == _session && Stage == EndAppStage.Reading)
                {
                    Ask(app, refusal, name, icon, helpers);
                }
            });
        });
    }

    private void Ask(ChosenApp app, EndRefusal refusal, string name, AppIcon? icon, int helpers)
    {
        _asked = app;
        _name = name;
        _askedToEnd = refusal == EndRefusal.None;
        QuestionPage page;
        if (_askedToEnd)
        {
            _program = Path.GetFileName(_held?.ProgramPath ?? string.Empty);
            _processes = helpers + 1;
            _notResponding = _system.IsNotResponding(app.AppWindow);
            page = EndPage();
            LogAsked(_logger, _notResponding, _processes);
        }
        else
        {
            page = CantEndPage(refusal, name);
            LogCantEnd(_logger, refusal);
        }

        var session = _session;
        _question = _host.Ask(page, icon, _system.WorkAreaOf(app.Window), answer => _post(() => OnAnswered(session, answer)));
        Stage = EndAppStage.Asking;
        _watch = _time.CreateTimer(_ => _post(() => OnWatch(session)), null, WatchInterval, WatchInterval);
    }

    private QuestionPage EndPage()
    {
        var text = Format(_notResponding ? Strings.EndAppQuestionNotResponding : Strings.EndAppQuestionResponding, _name);
        var footer = Format(Strings.EndAppQuestionProgram, _program) + " " + PluralStrings.Format("EndAppQuestionProcesses", _processes);
        return new QuestionPage(
            Format(Strings.EndAppQuestionHeading, _name),
            text,
            footer,
            [Strings.EndAppQuestionEnd, Strings.EndAppQuestionCancel],
            _notResponding ? EndButton : 1);
    }

    private static QuestionPage CantEndPage(EndRefusal refusal, string name)
    {
        var heading = refusal == EndRefusal.CantTellWhich || name.Length == 0 ? Strings.EndAppCantEndThisApp : Format(Strings.EndAppCantEndHeading, name);
        var text = refusal switch
        {
            EndRefusal.RunsAsAdministrator => Format(Strings.EndAppCantEndAdministrator, name),
            EndRefusal.FileExplorer => Strings.EndAppCantEndFileExplorer,
            EndRefusal.PartOfWindows => Format(Strings.EndAppCantEndWindows, name),
            EndRefusal.PartOfPcCompanion => Strings.EndAppCantEndPcCompanion,
            EndRefusal.NotAllowed => Format(Strings.EndAppCantEndNotAllowed, name),
            _ => Strings.EndAppCantEndCantTell,
        };

        return new QuestionPage(heading, text, null, [Strings.EndAppOpenTaskManager, Strings.EndAppClose], 1);
    }

    private void OnWatch(int session)
    {
        if (session != _session || Stage != EndAppStage.Asking)
        {
            return;
        }

        // In case the question took the front without the covering window hearing of it.
        HideChoosing();
        if (!_askedToEnd || _held is not { } held)
        {
            return;
        }

        if (held.HasExited)
        {
            LogExitedWhileAsked(_logger);
            _question?.Close();
            return;
        }

        var notResponding = _system.IsNotResponding(_asked.AppWindow);
        if (notResponding != _notResponding)
        {
            _notResponding = notResponding;
            _question?.Change(EndPage());
            LogStateChanged(_logger, notResponding);
        }
    }

    private void OnAnswered(int session, int answer)
    {
        if (session != _session || Stage != EndAppStage.Asking)
        {
            return;
        }

        _question = null;
        StopWatch();
        if (_askedToEnd && answer == EndButton)
        {
            End();
            return;
        }

        if (!_askedToEnd && answer == OpenTaskManagerButton)
        {
            _host.OpenTaskManager();
        }

        LogNotEnded(_logger);
        Finish();
    }

    private void End()
    {
        Stage = EndAppStage.Ending;
        HideChoosing();
        var held = _held!;
        _held = null;
        var name = _name;
        var session = _session;
        _host.Background(() =>
        {
            var watch = Stopwatch.StartNew();
            var ended = held.EndProcess(out var errorCode) && held.WaitForExit(EndWait);
            var helpers = AppChooser.Helpers(_system, held.Id, held.StartTime, held.ProgramPath);
            var helpersEnded = 0;
            foreach (var id in helpers)
            {
                using var helper = _system.Open(id, out _);
                if (helper is not null && helper.StartTime >= held.StartTime
                    && string.Equals(helper.ProgramPath, held.ProgramPath, StringComparison.OrdinalIgnoreCase)
                    && helper.EndProcess(out _) && helper.WaitForExit(EndWait))
                {
                    helpersEnded++;
                }
            }

            held.Dispose();
            var elapsed = watch.Elapsed;
            _post(() => OnEnded(session, name, ended, errorCode, helpersEnded, helpers.Count, elapsed));
        });
    }

    private void OnEnded(int session, string name, bool ended, int errorCode, int helpersEnded, int helpers, TimeSpan elapsed)
    {
        if (session != _session || Stage != EndAppStage.Ending)
        {
            return;
        }

        if (ended)
        {
            LogEnded(_logger, helpersEnded, helpers, elapsed.TotalMilliseconds);
        }
        else
        {
            LogStillRunning(_logger, errorCode);
            _host.Notify(Strings.EndAppName, Format(Strings.EndAppNotEnded, name));
        }

        Stage = EndAppStage.Idle;
    }

    private void OnIdle(int session)
    {
        if (session == _session && Stage == EndAppStage.Choosing)
        {
            Stop(StopReason.Idle);
        }
    }

    private void StillActive() => _idle?.Change(IdleLimit, Timeout.InfiniteTimeSpan);

    private void Stop(StopReason reason)
    {
        LogStopped(_logger, reason);
        Finish();
    }

    private void Finish()
    {
        StopIdle();
        StopWatch();
        _question?.Close();
        _question = null;
        HideChoosing();
        if (Stage != EndAppStage.Ending)
        {
            _held?.Dispose();
            _held = null;
            Stage = EndAppStage.Idle;
        }

        _framed = ChosenApp.Nothing;
        _asked = ChosenApp.Nothing;
        _windows = [];
    }

    private void HideChoosing()
    {
        _view.HideFrame();
        if (_view.IsCovering)
        {
            _view.HideCovering();
        }
    }

    private void StopIdle()
    {
        _idle?.Dispose();
        _idle = null;
    }

    private void StopWatch()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App started choosing an app, from the {Source}.")]
    private static partial void LogStarted(ILogger logger, EndAppSource source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App stopped choosing an app: {Reason}.")]
    private static partial void LogStopped(ILogger logger, StopReason reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App didn't start choosing: it's {Stage}.")]
    private static partial void LogBusy(ILogger logger, EndAppStage stage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "End App didn't start choosing: Windows didn't give its window the front.")]
    private static partial void LogNotInFront(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App asks whether to end the chosen app: not responding {NotResponding}, {Processes} processes.")]
    private static partial void LogAsked(ILogger logger, bool notResponding, int processes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App can't end the chosen app: {Refusal}.")]
    private static partial void LogCantEnd(ILogger logger, EndRefusal refusal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App's question changed: the app's not responding {NotResponding}.")]
    private static partial void LogStateChanged(ILogger logger, bool notResponding);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The app End App asked about exited by itself.")]
    private static partial void LogExitedWhileAsked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App ended nothing: the question was canceled or closed.")]
    private static partial void LogNotEnded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "End App ended the app and {HelpersEnded} of its {Helpers} helpers in {Milliseconds} ms.")]
    private static partial void LogEnded(ILogger logger, int helpersEnded, int helpers, double milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The app End App ended still runs after 5 s (error {ErrorCode}).")]
    private static partial void LogStillRunning(ILogger logger, int errorCode);
}

/// <summary>Where End App is in choosing, asking and ending.</summary>
public enum EndAppStage
{
    Idle,

    /// <summary>The covering window is up, and the frame follows the pointer and Tab.</summary>
    Choosing,

    /// <summary>An app was chosen; its name, icon and helpers are being read.</summary>
    Reading,

    /// <summary>The question is open.</summary>
    Asking,

    /// <summary>The app and its helpers are being ended.</summary>
    Ending,
}

/// <summary>What started choosing.</summary>
public enum EndAppSource
{
    Shortcut,
    Menu,
}

/// <summary>Why choosing stopped without an app.</summary>
public enum StopReason
{
    Escape,
    RightClick,
    Shortcut,
    LostFront,
    DisplaysChanged,
    Idle,
}
