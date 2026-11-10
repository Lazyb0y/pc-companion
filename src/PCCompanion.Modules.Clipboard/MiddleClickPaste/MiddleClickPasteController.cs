using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>A text box the pointer rests on, whose plain middle-click the pointer hook keeps.</summary>
/// <param name="Point">Where the pointer rested when the box was recognized.</param>
/// <param name="Area">The claimed area: the point, give or take 2 pixels.</param>
/// <param name="Root">The top-level window the box is in.</param>
/// <param name="Window">The box's own window, for Windows' own text boxes; otherwise 0.</param>
public sealed record ClaimedBox(Point Point, Rectangle Area, nint Root, nint Window);

/// <summary>How a paste put the text in, for the log.</summary>
public enum PasteWay
{
    /// <summary>It didn't get as far.</summary>
    None,

    /// <summary>By window messages to one of Windows' own text boxes.</summary>
    Message,

    /// <summary>As a drop.</summary>
    Drop,
}

/// <summary>How a middle-click on a text box ended, and where.</summary>
public sealed class PasteFinishedEventArgs(PasteOutcome outcome, Point point) : EventArgs
{
    public PasteOutcome Outcome { get; } = outcome;

    public Point Point { get; } = point;
}

/// <summary>
/// Finds text boxes under the resting pointer and claims them for the pointer hook, as
/// "Recognizing a text box" in docs/features/clipboard/middle-click-paste.md says, and pastes the
/// text selected in the window in front at a kept middle-click there, as "Which text" and "How the
/// text goes in" say. Use it on one thread, the one <paramref name="post"/> runs on; nothing it
/// does there waits for another program.
/// </summary>
/// <param name="post">Runs an action on this controller's thread, from the timers' and the other threads'.</param>
public sealed partial class MiddleClickPasteController(IMiddleClickPasteSystem system, TimeProvider time, Action<Action> post, ILogger logger) : IDisposable
{
    /// <summary>How long after the last pointer event the place under the pointer is checked.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>How long the wheel must be still before a text box is claimed again.</summary>
    public static readonly TimeSpan WheelQuiet = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a paste waits for the selected text.</summary>
    public static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(1.5);

    /// <summary>How long after raw input reports a middle-click its check waits for the hook to have seen it.</summary>
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(60);

    /// <summary>The most characters pasted.</summary>
    public const int MaxLength = 65_536;

    /// <summary>How far, in pixels, the pointer may move from the click before the text is dropped.</summary>
    public const int MoveTolerance = 4;

    // How far from the recognized point, in pixels, a click still counts as there.
    private const int SpotMargin = 2;

    private static readonly PointerTrigger PasteClick = new(PointerButton.Middle, PointerKeys.None);

    private readonly ExcludedWindows _excluded = new(system);
    private readonly List<uint> _clicks = [];
    private ITimer? _settleTimer;
    private ITimer? _limitTimer;
    private ITimer? _checkTimer;
    private Point? _lookedAt;
    private Point? _asking;
    private bool _askAgain;
    private long? _wheelTurnedAt;
    private Paste? _paste;
    private Task<SelectedTextResult>? _reading;
    private bool _stopped;

    /// <summary>Raised on this controller's thread as each middle-click on a claimed text box ends.</summary>
    public event EventHandler<PasteFinishedEventArgs>? Finished;

    /// <summary>The text box claimed now, or <see langword="null"/>.</summary>
    public ClaimedBox? Claimed { get; private set; }

    /// <summary>Whether a paste is being read or put in now.</summary>
    public bool IsPasting => _paste is not null;

    /// <summary>Takes the apps that keep their middle-click from <paramref name="settings"/>.</summary>
    public void Configure(MiddleClickPasteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The box the pointer rests on may be a newly listed app's, so it's looked at again.
        if (_excluded.SetApps(settings.ExcludedApps))
        {
            Release();
            _lookedAt = null;
            ScheduleSettle(SettleDelay);
        }
    }

    /// <summary>Call it for each pointer event that raw input reports.</summary>
    public void OnPointerInput(PointerInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Turning the wheel scrolls what's under the pointer, and a press of the wheel while
        // scrolling isn't a paste.
        if ((input.Changes & PointerButtonChanges.Wheel) != 0)
        {
            _wheelTurnedAt = time.GetTimestamp();
            _lookedAt = null;
            Release();
        }

        // A middle-click on the claimed box is checked a moment later: the hook may not have seen it.
        if ((input.Changes & PointerButtonChanges.MiddleDown) != 0 && Claimed is not null)
        {
            _clicks.Add(input.Time);
            _checkTimer ??= time.CreateTimer(_ => post(CheckClicks), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _checkTimer.Change(CheckDelay, Timeout.InfiniteTimeSpan);
        }

        if (Claimed is { } claimed && system.PointerPosition is { } point && !claimed.Area.Contains(point))
        {
            Release();
        }

        ScheduleSettle(SettleDelay);
    }

    /// <summary>
    /// Call it for each middle-click the pointer hook kept for this controller's claim: the text
    /// selected in the window in front goes into the claimed box, where the click was.
    /// </summary>
    public void OnKept(KeptPointerInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_stopped || input.Trigger != PasteClick || Claimed is not { } box || !box.Area.Contains(input.Point))
        {
            return;
        }

        var started = time.GetTimestamp();
        if (_paste is not null)
        {
            Report(PasteOutcome.Busy, input.Point, started, PasteWay.None);
            return;
        }

        // A read that its app hasn't answered still holds the reader; the app isn't asked again.
        if (_reading is { IsCompleted: false })
        {
            Report(PasteOutcome.AppNotResponding, input.Point, started, PasteWay.None);
            return;
        }

        var app = system.CheckWindow(system.FocusWindow);
        if (app != SelectedTextApp.Readable)
        {
            Report(OutcomeOf(app), input.Point, started, PasteWay.None);
            return;
        }

        var paste = new Paste(input.Point, box, started);
        _paste = paste;
        var reading = system.ReadAsync(input.Point);
        _reading = reading;
        _limitTimer ??= time.CreateTimer(_ => post(ReadTooSlow), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _limitTimer.Change(ReadLimit, Timeout.InfiniteTimeSpan);
        Then(reading, result => Read(paste, result));
    }

    /// <summary>Checks the place under the resting pointer, and claims it when it's a text box.</summary>
    public void Settle()
    {
        if (_stopped || system.PointerPosition is not { } point)
        {
            return;
        }

        if (Claimed is { } claimed)
        {
            if (claimed.Area.Contains(point))
            {
                return;
            }

            Release();
        }

        if (_wheelTurnedAt is { } turned && time.GetElapsedTime(turned) is var since && since < WheelQuiet)
        {
            ScheduleSettle(WheelQuiet - since);
            return;
        }

        if (system.IsMouseButtonHeld || point == _lookedAt)
        {
            return;
        }

        if (_asking is not null)
        {
            _askAgain = true;
            return;
        }

        _lookedAt = point;
        if (!system.IsTextPointer)
        {
            return;
        }

        var root = system.RootAt(point);
        if (root == 0)
        {
            return;
        }

        var app = system.CheckWindow(root);
        if (app != SelectedTextApp.Readable)
        {
            LogLeftAlone(logger, app);
            return;
        }

        if (_excluded.Contains(root))
        {
            LogListedApp(logger);
            return;
        }

        _asking = point;
        system.AskAsync(point).ContinueWith(task => post(() => Answered(point, root, task.Result)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Removes the claim and stops looking, as the feature stops; a paste under way pastes nothing more.</summary>
    public void Stop()
    {
        _stopped = true;
        _settleTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _limitTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _checkTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _paste = null;
        Release();
    }

    public void Dispose()
    {
        Stop();
        _settleTimer?.Dispose();
        _limitTimer?.Dispose();
        _checkTimer?.Dispose();
    }

    private static PasteOutcome OutcomeOf(SelectedTextApp app) => app switch
    {
        SelectedTextApp.NoWindow => PasteOutcome.NothingSelected,
        SelectedTextApp.NotResponding => PasteOutcome.AppNotResponding,
        SelectedTextApp.RunsAsAdministrator => PasteOutcome.AppRunsAsAdministrator,
        SelectedTextApp.RunsWithoutAdminRights => PasteOutcome.RunsAsAdministrator,
        SelectedTextApp.RunsWithFewerRights => PasteOutcome.AppHasFewerRights,
        _ => PasteOutcome.NotShared,
    };

    private static PasteOutcome? OutcomeOf(SelectedTextStatus status) => status switch
    {
        SelectedTextStatus.Read => null,
        SelectedTextStatus.NothingSelected or SelectedTextStatus.WholeBoxSelected => PasteOutcome.NothingSelected,
        SelectedTextStatus.Password => PasteOutcome.Password,
        SelectedTextStatus.TooLong => PasteOutcome.TooLong,
        SelectedTextStatus.TimedOut => PasteOutcome.AppNotResponding,
        _ => PasteOutcome.NotShared,
    };

    private static PasteOutcome OutcomeOf(TextDropResult result) => result switch
    {
        TextDropResult.Dropped => PasteOutcome.Pasted,
        TextDropResult.NotResponding => PasteOutcome.BoxNotResponding,
        TextDropResult.Busy => PasteOutcome.Busy,
        _ => PasteOutcome.BoxRefused,
    };

    private void ReadTooSlow()
    {
        if (_paste is { Text: null } paste && _reading is { IsCompleted: false })
        {
            Finish(paste, PasteOutcome.AppNotResponding, PasteWay.None);
        }
    }

    private void Read(Paste paste, SelectedTextResult result)
    {
        if (_paste != paste)
        {
            return;
        }

        _limitTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (OutcomeOf(result.Status) is { } refused)
        {
            Finish(paste, refused, PasteWay.None);
            return;
        }

        paste.Text = result.Text!;
        switch (system.KindOf(paste.Box.Window))
        {
            case EditBoxKind.Plain:
                Then(system.InsertAtAsync(paste.Box.Window, paste.Point, paste.Text), inserted =>
                {
                    if (inserted == EditBoxResult.NeedsPlacing)
                    {
                        PlaceAndInsert(paste);
                    }
                    else
                    {
                        AfterInsert(paste, inserted);
                    }
                });
                break;

            case EditBoxKind.Rich:
                PlaceAndInsert(paste);
                break;

            default:
                // The drop goes where the pointer is, so the pointer must still be at the click.
                if (system.PointerPosition is not { } now || Math.Abs(now.X - paste.Point.X) > MoveTolerance || Math.Abs(now.Y - paste.Point.Y) > MoveTolerance)
                {
                    Finish(paste, PasteOutcome.PointerMoved, PasteWay.Drop);
                    return;
                }

                Then(system.DropAsync(paste.Point, paste.Text), dropped =>
                {
                    // Apps take a drop without always putting the keyboard focus in the box, so
                    // it's put there, as after a paste by messages.
                    if (dropped == TextDropResult.Dropped)
                    {
                        Then(system.FocusAsync(paste.Box.Root, paste.Box.Window, paste.Point), _ => Finish(paste, PasteOutcome.Pasted, PasteWay.Drop));
                    }
                    else
                    {
                        Finish(paste, OutcomeOf(dropped), PasteWay.Drop);
                    }
                });
                break;
        }
    }

    // A rich edit box, or a plain one too long to place the cursor in by message.
    private void PlaceAndInsert(Paste paste) =>
        Then(system.PlaceCursorAsync(paste.Point), placed =>
        {
            if (!placed)
            {
                Finish(paste, PasteOutcome.BoxRefused, PasteWay.Message);
                return;
            }

            Then(system.InsertAtCursorAsync(paste.Box.Window, paste.Text!), inserted => AfterInsert(paste, inserted));
        });

    private void AfterInsert(Paste paste, EditBoxResult result)
    {
        if (result != EditBoxResult.Inserted)
        {
            Finish(paste, result == EditBoxResult.NotResponding ? PasteOutcome.BoxNotResponding : PasteOutcome.BoxRefused, PasteWay.Message);
            return;
        }

        Then(system.FocusAsync(paste.Box.Root, paste.Box.Window, paste.Point), _ => Finish(paste, PasteOutcome.Pasted, PasteWay.Message));
    }

    private void Finish(Paste paste, PasteOutcome outcome, PasteWay way)
    {
        if (_paste != paste)
        {
            return;
        }

        _paste = null;
        paste.Text = null;
        Report(outcome, paste.Point, paste.StartedAt, way);
    }

    private void Report(PasteOutcome outcome, Point point, long startedAt, PasteWay way)
    {
        var milliseconds = (long)time.GetElapsedTime(startedAt).TotalMilliseconds;
        LogPasteEnded(logger, outcome, way, milliseconds);
        Finished?.Invoke(this, new PasteFinishedEventArgs(outcome, point));
    }

    // A middle-click raw input reported on the claimed box that the hook never saw means Windows
    // removed the hook: it's set again, and that click pastes nothing.
    private void CheckClicks()
    {
        foreach (var click in _clicks)
        {
            if (system.Outcome(click) == PointerInputOutcome.NotSeen)
            {
                LogHookRemoved(logger);
                system.SetHookAgain();
                break;
            }
        }

        _clicks.Clear();
    }

    // Runs the next step on this controller's thread once the task, which never fails, ends.
    private void Then<T>(Task<T> task, Action<T> next) =>
        task.ContinueWith(done => post(() =>
        {
            if (!_stopped)
            {
                next(done.Result);
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void Answered(Point point, nint root, TextBoxAnswer answer)
    {
        _asking = null;
        if (_stopped)
        {
            return;
        }

        // The pointer moved on while the app answered, so the new place is looked at instead.
        if (_askAgain)
        {
            _askAgain = false;
            Settle();
            return;
        }

        if (answer.Status != TextBoxStatus.TextBox)
        {
            LogNotTextBox(logger, answer.Status, answer.ErrorCode);
            return;
        }

        var area = Rectangle.FromLTRB(point.X - SpotMargin, point.Y - SpotMargin, point.X + SpotMargin + 1, point.Y + SpotMargin + 1);
        if (system.PointerPosition is not { } now || !area.Contains(now))
        {
            ScheduleSettle(SettleDelay);
            return;
        }

        Claimed = new ClaimedBox(point, area, root, answer.Window);
        system.Claim(new PointerClaim(area, new HashSet<PointerTrigger> { PasteClick }));
        LogClaimed(logger, answer.Window != 0);
    }

    private void Release()
    {
        if (Claimed is null)
        {
            return;
        }

        Claimed = null;
        system.Claim(null);
        LogReleased(logger);
    }

    private void ScheduleSettle(TimeSpan delay)
    {
        if (_stopped)
        {
            return;
        }

        _settleTimer ??= time.CreateTimer(_ => post(Settle), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _settleTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    // One middle-click being pasted. The text stays only until the paste ends.
    private sealed class Paste(Point point, ClaimedBox box, long startedAt)
    {
        public Point Point { get; } = point;

        public ClaimedBox Box { get; } = box;

        public long StartedAt { get; } = startedAt;

        public string? Text { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste: {Outcome}, by {Way}, in {Milliseconds} ms")]
    private static partial void LogPasteEnded(ILogger logger, PasteOutcome outcome, PasteWay way, long milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows removed the pointer hook, so Middle-Click Paste set it again.")]
    private static partial void LogHookRemoved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste claimed the text box under the pointer, one of Windows' own: {OwnBox}")]
    private static partial void LogClaimed(ILogger logger, bool ownBox);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste removed its claim")]
    private static partial void LogReleased(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste left the place under the pointer alone: {Status} (0x{ErrorCode:X8})")]
    private static partial void LogNotTextBox(ILogger logger, TextBoxStatus status, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste left a window alone: {App}")]
    private static partial void LogLeftAlone(ILogger logger, SelectedTextApp app);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Middle-Click Paste left a window of a listed app alone")]
    private static partial void LogListedApp(ILogger logger);
}
