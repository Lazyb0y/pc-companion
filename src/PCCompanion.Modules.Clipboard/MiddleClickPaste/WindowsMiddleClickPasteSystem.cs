using System;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.MiddleClickPaste;

/// <summary>
/// Middle-Click Paste's view of Windows, through the shared pointer hook, the feature's reader and
/// its drop. Window messages to Windows' own text boxes go out from the thread pool, since they
/// wait for the box's app. Dispose it to end the drop's thread.
/// </summary>
public sealed class WindowsMiddleClickPasteSystem(PointerHook hook, SelectedTextReader reader) : IMiddleClickPasteSystem, IDisposable
{
    /// <summary>
    /// How long a browser that is turning on its accessibility support is asked again what's under
    /// the pointer, about 0.3 seconds in the spike.
    /// </summary>
    public static readonly TimeSpan AskRetryFor = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a browser that is turning on its accessibility support is asked again for its
    /// selection, so that its last answer still comes within <see cref="MiddleClickPasteController.ReadLimit"/>.
    /// </summary>
    public static readonly TimeSpan ReadRetryFor = TimeSpan.FromSeconds(1.3);

    // How long, and how often, the front is watched after a box with a window of its own is focused.
    private static readonly TimeSpan FrontWatch = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FrontCheck = TimeSpan.FromMilliseconds(30);

    private TextDrop? _drop;

    public Point? PointerPosition => InputState.PointerPosition;

    public bool IsTextPointer => InputState.IsTextPointer;

    public bool IsMouseButtonHeld => InputState.IsMouseButtonHeld;

    public nint FocusWindow => TextCursor.Read().Window;

    public nint RootAt(Point point) => WindowHitTest.WindowAt(point) is var window and not 0 ? WindowHitTest.Root(window) : 0;

    public SelectedTextApp CheckWindow(nint window) => SelectedText.CheckWindow(window);

    public Task<TextBoxAnswer> AskAsync(Point point) => reader.AskAsync(point, AskRetryFor);

    public void Claim(PointerClaim? claim) => hook.Claim(claim);

    public PointerInputOutcome Outcome(uint time) => PointerHook.Outcome(time);

    public void SetHookAgain() => PointerHook.SetAgain();

    public Task<SelectedTextResult> ReadAsync(Point clickedAt) => reader.ReadAsync(ReadRetryFor, MiddleClickPasteController.MaxLength, clickedAt);

    public EditBoxKind KindOf(nint window) => EditBoxes.KindOf(window);

    public Task<EditBoxResult> InsertAtAsync(nint box, Point point, string text) => Task.Run(() => EditBoxes.InsertAt(box, point, text));

    public Task<bool> PlaceCursorAsync(Point point) => reader.PlaceCursorAsync(point);

    public Task<EditBoxResult> InsertAtCursorAsync(nint box, string text) => Task.Run(() => EditBoxes.InsertAtCursor(box, text));

    public async Task<bool> FocusAsync(nint root, nint box, Point point)
    {
        Foreground.Bring(root);
        var focused = await reader.FocusAsync(point).ConfigureAwait(false);
        if (box == 0)
        {
            return focused;
        }

        // UI Automation focuses a box that has a window of its own, as Windows' own text boxes do,
        // by making that window the foreground window, a moment later (Sandbox checks). The
        // top-level window gets the front back, and the box keeps the focus.
        for (var watched = TimeSpan.Zero; watched < FrontWatch; watched += FrontCheck)
        {
            var front = Foreground.Window;
            if (front != 0 && front != root && WindowHitTest.Root(front) == root)
            {
                Foreground.Bring(root);
                break;
            }

            await Task.Delay(FrontCheck).ConfigureAwait(false);
        }

        return focused;
    }

    // The drop's thread starts at the first drop.
    public Task<TextDropResult> DropAsync(Point point, string text) => (_drop ??= new TextDrop()).DropAsync(point, text);

    public AppOfWindow? AppOf(nint root)
    {
        var processId = TopLevelWindows.ProcessIdOf(root);
        return processId == 0 ? null : new AppOfWindow(AppIdentity.OfWindow(root), AppIdentity.OfPackagedProcess(processId), AppIdentity.ProgramPath(processId), processId);
    }

    public bool Exists(nint window) => TopLevelWindows.Exists(window);

    public void Dispose() => _drop?.Dispose();
}
