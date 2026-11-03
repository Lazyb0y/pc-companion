using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Finds the title bar the pointer rests on, by asking the window under it, as "Recognizing a title
/// bar" in docs/features/windows/title-bar-actions.md says. Use it on one thread, the one its
/// <see cref="ITitleBarWindows"/> answers on.
/// </summary>
public sealed class TitleBarFinder(ITitleBarWindows windows, TimeProvider clock)
{
    /// <summary>How tall, in effective pixels, the band at the top of a window is that holds its title bar.</summary>
    public const int TopBand = 64;

    /// <summary>How long a window has to answer before the question is dropped.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromMilliseconds(150);

    private Question? _hover;
    private bool _moved;
    private int _generation;

    /// <summary>
    /// The window whose title bar the pointer rests on, and the point it answered for, or
    /// <see langword="null"/> when the pointer isn't on a title bar or the window hasn't answered yet.
    /// </summary>
    public TitleBarSpot? Spot { get; private set; }

    /// <summary>Raised when <see cref="Spot"/> changes.</summary>
    public event EventHandler? SpotChanged;

    /// <summary>
    /// Call it for each pointer event. When the pointer is in the top band of a window that may get
    /// actions, the window under it is asked what's there, with at most one question waiting.
    /// </summary>
    public void PointerMoved()
    {
        if (_hover is { } waiting)
        {
            _moved = true;
            if (clock.GetElapsedTime(waiting.Sent) < AnswerTimeout)
            {
                return;
            }

            // A window that doesn't answer in time: forget the question and ask again.
            _hover = null;
        }

        AskAtPointer();
    }

    /// <summary>
    /// Asks whether <paramref name="point"/> is on a title bar, as for a click that reached the app.
    /// <paramref name="onTitleBar"/> gets the window when it answers that it is, within
    /// <see cref="AnswerTimeout"/>; otherwise it isn't called.
    /// </summary>
    public void Check(Point point, Action<nint> onTitleBar)
    {
        ArgumentNullException.ThrowIfNull(onTitleBar);

        if (Find(point) is { } found)
        {
            CheckOnce(found.Under, found.Root, point, onTitleBar, clock.GetTimestamp(), _generation);
        }
    }

    /// <summary>Forgets the spot and every question waiting; answers that come later are dropped.</summary>
    public void Stop()
    {
        _generation++;
        _hover = null;
        _moved = false;
        SetSpot(null);
    }

    private void AskAtPointer()
    {
        _moved = false;
        if (windows.PointerPosition is not { } point || Find(point) is not { } found)
        {
            SetSpot(null);
            return;
        }

        if (Spot is { } spot && spot.Point == point && spot.Window == found.Root)
        {
            return;
        }

        Ask(new Question(found.Under, found.Root, point, clock.GetTimestamp()));
    }

    private void Ask(Question question)
    {
        _hover = question;
        if (!windows.Ask(question.Asked, question.Point, answer => OnAnswer(question, answer)) && ReferenceEquals(_hover, question))
        {
            _hover = null;
            SetSpot(null);
        }
    }

    private void OnAnswer(Question question, HitTestAnswer answer)
    {
        if (!ReferenceEquals(question, _hover))
        {
            return;
        }

        if (answer == HitTestAnswer.Transparent && question.Asked != question.Root && windows.Parent(question.Asked) is var parent and not 0)
        {
            Ask(question with { Asked = parent });
            return;
        }

        _hover = null;
        var still = windows.PointerPosition == question.Point;
        SetSpot(answer == HitTestAnswer.Caption && still ? new TitleBarSpot(question.Root, question.Point) : null);

        // Raw input reports a move before the pointer's position follows it, so the answer can be
        // about where the pointer was.
        if (_moved || !still)
        {
            AskAtPointer();
        }
    }

    private void CheckOnce(nint asked, nint root, Point point, Action<nint> onTitleBar, long sent, int generation)
    {
        windows.Ask(asked, point, answer =>
        {
            if (generation != _generation || clock.GetElapsedTime(sent) >= AnswerTimeout)
            {
                return;
            }

            if (answer == HitTestAnswer.Transparent && asked != root && windows.Parent(asked) is var parent and not 0)
            {
                CheckOnce(parent, root, point, onTitleBar, sent, generation);
            }
            else if (answer == HitTestAnswer.Caption)
            {
                onTitleBar(root);
            }
        });
    }

    // The window under the point and its top-level window, when the point is in that window's top band.
    private (nint Under, nint Root)? Find(Point point)
    {
        var under = windows.WindowAt(point);
        var root = under == 0 ? 0 : windows.Root(under);
        if (root == 0 || !windows.IsCandidate(root) || windows.VisibleFrame(root) is not { } frame || !frame.Contains(point))
        {
            return null;
        }

        var band = TopBand * windows.Dpi(root) / 96.0;
        return point.Y < frame.Top + band ? (under, root) : null;
    }

    private void SetSpot(TitleBarSpot? spot)
    {
        if (spot == Spot)
        {
            return;
        }

        Spot = spot;
        SpotChanged?.Invoke(this, EventArgs.Empty);
    }

    // A question about one point, to one window, for a top-level window's title bar.
    private sealed record Question(nint Asked, nint Root, Point Point, long Sent);
}

/// <summary>A point on a window's title bar, which the window answered for.</summary>
/// <param name="Window">The top-level window.</param>
/// <param name="Point">The point, in physical pixels.</param>
public sealed record TitleBarSpot(nint Window, Point Point);
