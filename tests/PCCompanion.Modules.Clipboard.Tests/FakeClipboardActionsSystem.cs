using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;

namespace PCCompanion.Modules.Clipboard.Tests;

/// <summary>
/// Clipboard Actions' Windows parts, recorded instead of used: no shortcut is registered, the
/// clipboard isn't read, no menu opens and no program starts. It may be used from the feature's
/// thread while a test waits for it. Its events are what happened, such as "register", "read",
/// "ask" for the app's text cursor, "menu", "allow front" or "dispose shortcut".
/// </summary>
internal sealed class FakeClipboardActionsSystem : RecordingFake<FakeClipboardActionsSystem>, IClipboardActionsSystem
{
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

    /// <summary>The items of each menu shown.</summary>
    public IReadOnlyList<IReadOnlyList<PopupMenuItem>> Menus
    {
        get
        {
            lock (Gate)
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

        lock (Gate)
        {
            Shortcut = new FakeShortcut(this, window);
            Changed();
            return Shortcut;
        }
    }

    public ClipboardTextResult ReadClipboard(MessageWindow window)
    {
        Record("read");
        return ReadFailure is { } failure ? throw failure : Clipboard;
    }

    public Point PointerPosition() => new(10, 20);

    /// <summary>The app in front and Windows' text cursor of it; by default, no window.</summary>
    public ForegroundTextCursor Front { get; set; }

    /// <summary>Whether the app in front runs at PC Companion's integrity level.</summary>
    public bool SameIntegrityLevel { get; set; } = true;

    /// <summary>The app's answer when it's asked for its text cursor; it never comes while unset.</summary>
    public Task<Rectangle?> AppAnswer { get; set; } = new TaskCompletionSource<Rectangle?>().Task;

    public ForegroundTextCursor ReadTextCursor() => Front;

    public bool CanAskTextCursor(nint window) => SameIntegrityLevel;

    public Task<Rectangle?> AskTextCursor(nint window)
    {
        Record($"ask {window}");
        return AppAnswer;
    }

    public uint ShowMenu(MessageWindow window, IReadOnlyList<PopupMenuItem> items, Point position, Rectangle? keepClear)
    {
        lock (Gate)
        {
            _menus.Add(items);
        }

        Record(keepClear is { } line
            ? $"menu at {position.X},{position.Y} below {line.Left},{line.Top},{line.Right},{line.Bottom}"
            : $"menu at {position.X},{position.Y}");
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
        lock (Gate)
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

    /// <summary>The shortcut, pressed by the test on the feature's thread, as Windows would report it.</summary>
    public sealed class FakeShortcut(FakeClipboardActionsSystem system, MessageWindow window) : IShortcut
    {
        public event EventHandler? Pressed;

        public void Press() => window.BeginInvoke(() => Pressed?.Invoke(this, EventArgs.Empty));

        public void Dispose() => system.Record("dispose shortcut");
    }
}
