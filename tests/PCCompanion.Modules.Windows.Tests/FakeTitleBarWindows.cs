using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows, each a rectangle with an optional parent, that answer where their title bar is
/// as each test sets. With <see cref="AnswersLater"/>, an answer waits until the test calls
/// <see cref="Answer"/>, as another program answers when it handles its messages.
/// </summary>
internal sealed class FakeTitleBarWindows : ITitleBarWindows
{
    private readonly List<(nint Handle, Rectangle Bounds, nint Parent, uint Dpi)> _windows = [];
    private readonly Queue<Action> _waiting = new();

    public Point? PointerPosition { get; set; }

    /// <summary>What each window answers at a point; elsewhere unless a test says so.</summary>
    public Func<nint, Point, HitTestAnswer> Answers { get; set; } = (_, _) => HitTestAnswer.Elsewhere;

    public HashSet<nint> NotCandidates { get; } = [];

    public List<(nint Window, Point Point)> Asked { get; } = [];

    public bool AnswersLater { get; set; }

    public bool Refuses { get; set; }

    public int Waiting => _waiting.Count;

    /// <summary>Adds a window; a child is found before its parent where they overlap.</summary>
    public void Add(nint handle, Rectangle bounds, nint parent = 0, uint dpi = 96) => _windows.Add((handle, bounds, parent, dpi));

    /// <summary>Delivers the oldest answer that waits.</summary>
    public void Answer() => _waiting.Dequeue()();

    public nint WindowAt(Point point) =>
        _windows.Where(window => window.Bounds.Contains(point)).OrderByDescending(window => Depth(window.Handle)).Select(window => window.Handle).FirstOrDefault();

    public nint Root(nint window)
    {
        var current = window;
        while (Parent(current) is var parent and not 0)
        {
            current = parent;
        }

        return current;
    }

    public nint Parent(nint window) => _windows.FirstOrDefault(other => other.Handle == window).Parent;

    public Rectangle? VisibleFrame(nint window) => _windows.Any(other => other.Handle == window) ? _windows.First(other => other.Handle == window).Bounds : null;

    public uint Dpi(nint window) => _windows.FirstOrDefault(other => other.Handle == window).Dpi;

    public bool IsCandidate(nint root) => !NotCandidates.Contains(root);

    public bool Ask(nint window, Point point, Action<HitTestAnswer> answered)
    {
        if (Refuses)
        {
            return false;
        }

        Asked.Add((window, point));
        var answer = Answers(window, point);
        if (AnswersLater)
        {
            _waiting.Enqueue(() => answered(answer));
        }
        else
        {
            answered(answer);
        }

        return true;
    }

    private int Depth(nint window) => Parent(window) is var parent and not 0 ? 1 + Depth(parent) : 0;
}
