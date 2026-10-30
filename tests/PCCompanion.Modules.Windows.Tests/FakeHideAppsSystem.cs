using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Windows.HideApps;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>
/// Made-up windows in an order, front to back, that minimize, restore and move as Windows would
/// make them, and a record of what Hide Apps did to them. With <see cref="MovesLater"/>, a move
/// waits until the test calls <see cref="ApplyMoves"/>, as another program's window moves when its
/// program handles its messages.
/// </summary>
internal sealed class FakeHideAppsSystem : IHideAppsSystem
{
    public static readonly Rectangle MainDisplay = new(0, 0, 1920, 1080);

    private readonly List<(nint Window, nint InFront)> _pendingMoves = [];

    /// <summary>The windows, front to back.</summary>
    public List<FakeHideWindow> Order { get; } = [];

    public List<string> Calls { get; } = [];

    public HashSet<uint> Watched { get; } = [];

    public bool MovesLater { get; set; }

    public bool WatchesOrder { get; private set; }

    public bool WatchesRestores { get; private set; }

    public int Warnings { get; private set; }

    public nint ForegroundWindow { get; set; }

    /// <summary>Adds a window behind the others, of an app named after its program.</summary>
    public FakeHideWindow Add(nint handle, uint processId = 10, string program = @"C:\Apps\App.exe")
    {
        var window = new FakeHideWindow { Handle = handle, ProcessId = processId, Program = program };
        Order.Add(window);
        return window;
    }

    public FakeHideWindow this[nint handle] => Order.Single(window => window.Handle == handle);

    /// <summary>The handles front to back.</summary>
    public IEnumerable<nint> Handles => Order.Select(window => window.Handle);

    public IReadOnlyList<TopLevelWindow> ListWindows() =>
        [.. Order.Where(window => window.Visible && !window.Closed).Select(window => new TopLevelWindow(
            window.Handle,
            window.ProcessId,
            IsMinimized: window.Minimized,
            IsCloaked: window.Cloaked,
            IsToolWindow: window.ToolWindow,
            IsAppWindow: false,
            IsNoActivate: false,
            IsTopmost: window.Topmost,
            HasOwner: window.Owner != 0,
            IsDesktop: window.Desktop))];

    public nint RootOwner(nint window)
    {
        while (Find(window) is { Owner: not 0 } listed)
        {
            window = listed.Owner;
        }

        return window;
    }

    public bool Exists(nint window) => Find(window) is { Closed: false };

    public bool IsMinimized(nint window) => Find(window) is { Minimized: true };

    public bool IsEnabled(nint window) => Find(window) is { DialogOpen: false };

    public bool HasMinimizeButton(nint window) => Find(window) is { MinimizeButton: true };

    public string ClassName(nint window) => Find(window)?.ClassName ?? string.Empty;

    public Rectangle? DisplayOf(nint window) => Find(window)?.Display;

    public bool IsNotResponding(nint window) => Find(window) is { NotResponding: true };

    public string? WindowAppId(nint window) => Find(window)?.AppId;

    public string? PackageAppId(uint processId) => null;

    public string? ProgramPath(uint processId) => Order.FirstOrDefault(window => window.ProcessId == processId)?.Program;

    public bool Minimize(nint window)
    {
        if (!Change(window, "minimize", listed => listed.Minimized = true))
        {
            return false;
        }

        // Windows brings the next window to the front.
        ForegroundWindow = Order.FirstOrDefault(listed => listed.Visible && !listed.Minimized && !listed.Closed && listed.Handle != window)?.Handle ?? 0;
        return true;
    }

    public bool MinimizeWithoutActivating(nint window) => Change(window, "minimize quietly", listed => listed.Minimized = true);

    public bool RestoreWithoutActivating(nint window) => Change(window, "restore", listed =>
    {
        listed.Minimized = false;
        if (listed.MovesToTopOnRestore)
        {
            Order.Remove(listed);
            Order.Insert(0, listed);
        }
    });

    public bool AskToMinimize(nint window) => Ask(window, "ask minimize", listed => listed.Minimized = true);

    public bool AskToRestore(nint window) => Ask(window, "ask restore", listed =>
    {
        listed.Minimized = false;
        ForegroundWindow = listed.Handle;
    });

    public bool PutBehind(nint window, nint inFront)
    {
        Calls.Add($"behind {window} {inFront}");
        var listed = Find(window);
        if (listed is null || listed.Closed || listed.RunsAsAdministrator)
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

    public bool IsAppWindow(nint window) => Find(window) is { Visible: true, Closed: false, Cloaked: false, ToolWindow: false, Desktop: false, Owner: 0 };

    public bool Activate(nint window)
    {
        Calls.Add($"activate {window}");
        ForegroundWindow = window;
        return true;
    }

    public void PlayWarning() => Warnings++;

    public void WatchRestores(bool watch)
    {
        Calls.Add($"watch restores {watch}");
        WatchesRestores = watch;
    }

    public void Watch(uint processId)
    {
        Calls.Add($"watch {processId}");
        Watched.Add(processId);
    }

    public void Unwatch(uint processId)
    {
        Calls.Add($"unwatch {processId}");
        Watched.Remove(processId);
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

    // Windows refuses an app running as administrator anything but requests.
    private bool Change(nint window, string call, System.Action<FakeHideWindow> change)
    {
        Calls.Add($"{call} {window}");
        if (Find(window) is not { Closed: false, RunsAsAdministrator: false } listed)
        {
            return false;
        }

        change(listed);
        return true;
    }

    private bool Ask(nint window, string call, System.Action<FakeHideWindow> change)
    {
        Calls.Add($"{call} {window}");
        if (Find(window) is not { Closed: false, RefusesRequests: false } listed)
        {
            return false;
        }

        if (!listed.IgnoresRequests)
        {
            change(listed);
        }

        return true;
    }

    // A window's owned windows move with it, and stay in front of it.
    private void Move(nint window, nint inFront)
    {
        var moved = Order.Where(listed => listed.Handle == window || RootOwner(listed.Handle) == window).ToList();
        Order.RemoveAll(moved.Contains);
        Order.InsertRange(Order.FindIndex(listed => listed.Handle == inFront) + 1, moved.OrderBy(listed => listed.Handle == window));
    }
}

internal sealed class FakeHideWindow
{
    public nint Handle { get; init; }

    public uint ProcessId { get; init; }

    public string? Program { get; init; }

    public string? AppId { get; init; }

    public string ClassName { get; init; } = "AppWindow";

    public nint Owner { get; init; }

    public bool ToolWindow { get; init; }

    public bool Desktop { get; init; }

    public Rectangle Display { get; set; } = FakeHideAppsSystem.MainDisplay;

    public bool Visible { get; set; } = true;

    public bool Minimized { get; set; }

    public bool Cloaked { get; set; }

    public bool Topmost { get; set; }

    public bool MinimizeButton { get; set; } = true;

    public bool DialogOpen { get; set; }

    public bool Closed { get; set; }

    public bool NotResponding { get; set; }

    /// <summary>Windows refuses to minimize, restore or move it for PC Companion, which can only ask its app.</summary>
    public bool RunsAsAdministrator { get; set; }

    /// <summary>Its app ignores requests to minimize and restore.</summary>
    public bool IgnoresRequests { get; set; }

    /// <summary>Windows refuses even to pass on requests to its app.</summary>
    public bool RefusesRequests { get; set; }

    /// <summary>Windows accepts the move, but the window stays where it is.</summary>
    public bool NeverMoves { get; set; }

    /// <summary>Its app puts it in front of the others as it comes back, as dialog-based apps can.</summary>
    public bool MovesToTopOnRestore { get; set; }
}
