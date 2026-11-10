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

/// <summary>
/// Finds text boxes under the resting pointer and claims them for the pointer hook, as
/// "Recognizing a text box" in docs/features/clipboard/middle-click-paste.md says. Use it on one
/// thread, the one <paramref name="post"/> runs on.
/// </summary>
/// <param name="post">Runs an action on this controller's thread, from the timer's and the reader's.</param>
public sealed partial class MiddleClickPasteController(IMiddleClickPasteSystem system, TimeProvider time, Action<Action> post, ILogger logger) : IDisposable
{
    /// <summary>How long after the last pointer event the place under the pointer is checked.</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>How long the wheel must be still before a text box is claimed again.</summary>
    public static readonly TimeSpan WheelQuiet = TimeSpan.FromMilliseconds(500);

    // How far from the recognized point, in pixels, a click still counts as there.
    private const int SpotMargin = 2;

    private static readonly PointerTrigger PasteClick = new(PointerButton.Middle, PointerKeys.None);

    private readonly ExcludedWindows _excluded = new(system);
    private ITimer? _settleTimer;
    private Point? _lookedAt;
    private Point? _asking;
    private bool _askAgain;
    private long? _wheelTurnedAt;
    private bool _stopped;

    /// <summary>The text box claimed now, or <see langword="null"/>.</summary>
    public ClaimedBox? Claimed { get; private set; }

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

        if (Claimed is { } claimed && system.PointerPosition is { } point && !claimed.Area.Contains(point))
        {
            Release();
        }

        ScheduleSettle(SettleDelay);
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

    /// <summary>Removes the claim and stops looking, as the feature stops.</summary>
    public void Stop()
    {
        _stopped = true;
        _settleTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Release();
    }

    public void Dispose()
    {
        Stop();
        _settleTimer?.Dispose();
    }

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
