using System.Collections.Generic;
using System.Linq;
using PCCompanion.Modules.Windows.HideApps;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows in an order, front to back, that restore and move as Windows would make them,
/// and a record of what Hide Apps did to them. With <see cref="MovesLater"/>, a move waits until
/// the test calls <see cref="ApplyMoves"/>, as another program's window moves when its program
/// handles its messages.
/// </summary>
internal sealed class FakeHideAppsSystem : IHideAppsSystem
{
    private readonly List<(nint Window, nint InFront)> _pendingMoves = [];

    /// <summary>The windows, front to back.</summary>
    public List<FakeHideWindow> Order { get; } = [];

    public List<string> Calls { get; } = [];

    public bool MovesLater { get; set; }

    public bool WatchesOrder { get; private set; }

    public FakeHideWindow Add(nint handle)
    {
        var window = new FakeHideWindow { Handle = handle };
        Order.Add(window);
        return window;
    }

    public FakeHideWindow this[nint handle] => Order.Single(window => window.Handle == handle);

    /// <summary>The handles front to back.</summary>
    public IEnumerable<nint> Handles => Order.Select(window => window.Handle);

    public bool Exists(nint window) => Find(window) is { Closed: false };

    public bool IsMinimized(nint window) => Find(window) is { Minimized: true };

    public bool IsNotResponding(nint window) => Find(window) is { NotResponding: true };

    public bool RestoreWithoutActivating(nint window)
    {
        Calls.Add($"restore {window}");
        if (Find(window) is not { Closed: false } listed)
        {
            return false;
        }

        listed.Minimized = false;
        return true;
    }

    public bool PutBehind(nint window, nint inFront)
    {
        Calls.Add($"behind {window} {inFront}");
        var listed = Find(window);
        if (listed is null || listed.Closed || listed.RefusesMoves)
        {
            return false;
        }

        if (listed.NeverMoves)
        {
            return true;
        }

        if (MovesLater)
        {
            _pendingMoves.Add((window, inFront));
        }
        else
        {
            Move(window, inFront);
        }

        return true;
    }

    public nint NextVisibleBehind(nint window)
    {
        var at = Order.FindIndex(listed => listed.Handle == window);
        return at < 0 ? 0 : Order.Skip(at + 1).FirstOrDefault(listed => listed.Visible)?.Handle ?? 0;
    }

    public void WatchOrder(bool watch)
    {
        Calls.Add($"watch order {watch}");
        WatchesOrder = watch;
    }

    /// <summary>Carries out the moves asked for so far.</summary>
    public void ApplyMoves()
    {
        foreach (var (window, inFront) in _pendingMoves.ToList())
        {
            Move(window, inFront);
        }

        _pendingMoves.Clear();
    }

    private FakeHideWindow? Find(nint window) => Order.FirstOrDefault(listed => listed.Handle == window);

    private void Move(nint window, nint inFront)
    {
        var moved = this[window];
        Order.Remove(moved);
        Order.Insert(Order.FindIndex(listed => listed.Handle == inFront) + 1, moved);
    }
}

internal sealed class FakeHideWindow
{
    public nint Handle { get; init; }

    public bool Visible { get; set; } = true;

    public bool Minimized { get; set; }

    public bool Closed { get; set; }

    public bool NotResponding { get; set; }

    /// <summary>Windows refuses to move it, as for an app running as administrator.</summary>
    public bool RefusesMoves { get; set; }

    /// <summary>Windows accepts the move, but the window stays where it is.</summary>
    public bool NeverMoves { get; set; }
}
