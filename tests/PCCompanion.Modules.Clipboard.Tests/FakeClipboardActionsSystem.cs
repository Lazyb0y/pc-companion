using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.Tests;

/// <summary>
/// Clipboard Actions' Windows parts, recorded instead of used: no shortcut is registered, the
/// clipboard isn't read, no menu opens and no program starts. It may be used from the feature's
/// thread while a test waits for it with <see cref="WaitUntil"/>.
/// </summary>
internal sealed class FakeClipboardActionsSystem : IClipboardActionsSystem
{
    private readonly object _gate = new();
    private readonly List<string> _events = [];
    private readonly List<IReadOnlyList<PopupMenuItem>> _menus = [];

    public bool IsAdministrator { get; set; }

    /// <summary>Whether Windows refuses the shortcut.</summary>
    public bool RefusesShortcut { get; set; }

    /// <summary>What the clipboard holds.</summary>
    public ClipboardTextResult Clipboard { get; set; } = new(ClipboardTextStatus.NoText);

    /// <summary>Thrown when the clipboard is read, to fail the feature's thread.</summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>Chooses an item of the menu shown, by its text; null closes the menu without a choice.</summary>
    public string? Choice { get; set; }

    /// <summary>The shortcut registered last.</summary>
    public FakeShortcut? Shortcut { get; private set; }

    /// <summary>What happened, such as "register", "read", "menu", "allow front" or "dispose shortcut", in order.</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>The items of each menu shown.</summary>
    public IReadOnlyList<IReadOnlyList<PopupMenuItem>> Menus
    {
        get
        {
            lock (_gate)
            {
                return [.. _menus];
            }
        }
    }

    public List<ProcessStartInfo> Started { get; } = [];

    public IShortcut? RegisterShortcut(MessageWindow window)
    {
        Record(RefusesShortcut ? "refused" : "register");
        if (RefusesShortcut)
        {
            return null;
        }

        lock (_gate)
        {
            Shortcut = new FakeShortcut(this, window);
            Monitor.PulseAll(_gate);
            return Shortcut;
        }
    }

    public ClipboardTextResult ReadClipboard(MessageWindow window)
    {
        Record("read");
        return ReadFailure is { } failure ? throw failure : Clipboard;
    }

    public Point PointerPosition() => new(10, 20);

    public uint ShowMenu(MessageWindow window, IReadOnlyList<PopupMenuItem> items, Point position)
    {
        lock (_gate)
        {
            _menus.Add(items);
        }

        Record($"menu at {position.X},{position.Y}");
        foreach (var item in items)
        {
            if (item.Text == Choice && item.IsEnabled)
            {
                return item.Id;
            }
        }

        return 0;
    }

    public void AllowFront() => Record("allow front");

    public bool FileExists(string path) => true;

    public void StartProcess(ProcessStartInfo startInfo)
    {
        lock (_gate)
        {
            Started.Add(startInfo);
        }

        Record("start");
    }

    // Runs at once, on the feature's thread, so a test sees the action's result as soon as the menu closes.
    // Recorded after the action, since a test that waits for "background" then checks its result.
    public void RunInBackground(Action action)
    {
        action();
        Record("background");
    }

    /// <summary>Waits until <paramref name="condition"/> holds, up to <paramref name="timeout"/> or 10 seconds.</summary>
    public bool WaitUntil(Func<FakeClipboardActionsSystem, bool> condition, TimeSpan? timeout = null)
    {
        var deadline = Environment.TickCount64 + (long)(timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds;
        lock (_gate)
        {
            while (!condition(this))
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0)
                {
                    return false;
                }

                Monitor.Wait(_gate, TimeSpan.FromMilliseconds(Math.Min(left, 100)));
            }

            return true;
        }
    }

    public void Record(string happened)
    {
        lock (_gate)
        {
            _events.Add(happened);
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>The shortcut, pressed by the test on the feature's thread, as Windows would report it.</summary>
    public sealed class FakeShortcut(FakeClipboardActionsSystem system, MessageWindow window) : IShortcut
    {
        public event EventHandler? Pressed;

        public void Press() => window.BeginInvoke(() => Pressed?.Invoke(this, EventArgs.Empty));

        public void Dispose() => system.Record("dispose shortcut");
    }
}
