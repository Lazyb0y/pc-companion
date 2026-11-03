using System;
using System.Drawing;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.TitleBarActions;

/// <summary>
/// Finds the title bar the pointer rests on, by asking the window under it, as "Recognizing a title
/// bar" in docs/features/windows/title-bar-actions.md says. Use it on one thread, the one its
/// <see cref="ITitleBarWindows"/> answers on.
/// </summary>
/// <param name="excluded">The windows of the apps that keep their title-bar clicks, which aren't asked.</param>
public sealed class TitleBarFinder(ITitleBarWindows windows, ExcludedWindows excluded, TimeProvider clock)
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
    /// <see cref="AnswerTimeout"/>; otherwise it isn't called. Returns whether the window there was
    /// asked, and why not.
    /// </summary>
    public TitleBarCheck Check(Point point, Action<nint> onTitleBar)
    {
        ArgumentNullException.ThrowIfNull(onTitleBar);

        var (found, under, root) = Look(point);
        if (found == TitleBarCheck.Asked && !CheckOnce(under, root, point, onTitleBar, clock.GetTimestamp(), _generation))
        {
            return TitleBarCheck.Refused;
        }

        return found;
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
        if (windows.PointerPosition is not { } point || Look(point) is not (TitleBarCheck.Asked, var under, var root))
        {
            SetSpot(null);
            return;
        }

        if (Spot is { } spot && spot.Point == point && spot.Window == root)
        {
            return;
        }

        Ask(new Question(under, root, point, clock.GetTimestamp()));
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

    // Returns false when Windows refused the question.
    private bool CheckOnce(nint asked, nint root, Point point, Action<nint> onTitleBar, long sent, int generation) =>
        windows.Ask(asked, point, answer =>
        {
            if (generation != _generation || clock.GetElapsedTime(sent) >= AnswerTimeout)
            {
                return;
            }

            if (answer == HitTestAnswer.Transparent && asked != root && windows.Parent(asked) is var parent and not 0)
            {
                _ = CheckOnce(parent, root, point, onTitleBar, sent, generation);
            }
            else if (answer == HitTestAnswer.Caption)
            {
                onTitleBar(root);
            }
        });

    // The window under the point and its top-level window, to ask, when the point is in that
    // window's top band and the window may get actions; otherwise why not. The band, which most
    // pointer moves are outside, is checked first, and the list of apps last.
    private (TitleBarCheck Found, nint Under, nint Root) Look(Point point)
    {
        var under = windows.WindowAt(point);
        var root = under == 0 ? 0 : windows.Root(under);
        if (root == 0 || windows.VisibleFrame(root) is not { } frame || !frame.Contains(point) || point.Y >= frame.Top + (TopBand * windows.Dpi(root) / 96.0))
        {
            return (TitleBarCheck.Elsewhere, 0, 0);
        }

        return !windows.IsCandidate(root) ? (TitleBarCheck.WindowLeftOut, 0, 0)
            : excluded.Contains(root) ? (TitleBarCheck.AppListed, 0, 0)
            : (TitleBarCheck.Asked, under, root);
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

/// <summary>What <see cref="TitleBarFinder.Check"/> found at a point.</summary>
public enum TitleBarCheck
{
    /// <summary>The window there was asked whether the point is on its title bar.</summary>
    Asked,

    /// <summary>No window's top band is there.</summary>
    Elsewhere,

    /// <summary>The window there gets no actions, as one that stopped responding or has a dialog open.</summary>
    WindowLeftOut,

    /// <summary>The window's app is on the list of apps that keep their title-bar clicks.</summary>
    AppListed,

    /// <summary>Windows refused the question, as for an app running as administrator while PC Companion doesn't.</summary>
    Refused,
}

/// <summary>A point on a window's title bar, which the window answered for.</summary>
/// <param name="Window">The top-level window.</param>
/// <param name="Point">The point, in physical pixels.</param>
public sealed record TitleBarSpot(nint Window, Point Point);
