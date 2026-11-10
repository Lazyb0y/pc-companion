using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Modules.Clipboard.MiddleClickPaste;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Clipboard.Tests;

// Windows and apps as Middle-Click Paste sees them; each question waits until the test answers it.
internal sealed class FakeMiddleClickPasteSystem : IMiddleClickPasteSystem
{
    public Point? PointerPosition { get; set; } = new Point(100, 100);

    public bool IsTextPointer { get; set; } = true;

    public bool IsMouseButtonHeld { get; set; }

    public nint Root { get; set; } = 10;

    public SelectedTextApp App { get; set; } = SelectedTextApp.Readable;

    public Dictionary<nint, AppOfWindow> Apps { get; } = [];

    public List<Point> Asked { get; } = [];

    public List<TaskCompletionSource<TextBoxAnswer>> Questions { get; } = [];

    public List<PointerClaim?> Claims { get; } = [];

    public PointerClaim? Claim => Claims.Count == 0 ? null : Claims[^1];

    public nint RootAt(Point point) => Root;

    // Each window's rights; windows not given here have App's.
    public Dictionary<nint, SelectedTextApp> WindowApps { get; } = [];

    public SelectedTextApp CheckWindow(nint window) => WindowApps.GetValueOrDefault(window, App);

    public Task<TextBoxAnswer> AskAsync(Point point)
    {
        Asked.Add(point);
        var question = new TaskCompletionSource<TextBoxAnswer>();
        Questions.Add(question);
        return question.Task;
    }

    void IMiddleClickPasteSystem.Claim(PointerClaim? claim) => Claims.Add(claim);

    public Dictionary<uint, PointerInputOutcome> Outcomes { get; } = [];

    public int HookSetAgain { get; private set; }

    public nint FocusWindow { get; set; } = 20;

    public List<Point> Reads { get; } = [];

    public List<TaskCompletionSource<SelectedTextResult>> PendingReads { get; } = [];

    public Dictionary<nint, EditBoxKind> Kinds { get; } = [];

    public EditBoxResult InsertResult { get; set; } = EditBoxResult.Inserted;

    public EditBoxResult CursorInsertResult { get; set; } = EditBoxResult.Inserted;

    public bool Placed { get; set; } = true;

    public TextDropResult DropResult { get; set; } = TextDropResult.Dropped;

    // Each step in the order the controller took it, such as "insert 55 at 100,100: text".
    public List<string> Steps { get; } = [];

    public PointerInputOutcome Outcome(uint time) => Outcomes.GetValueOrDefault(time, PointerInputOutcome.Kept);

    public void SetHookAgain() => HookSetAgain++;

    public Task<SelectedTextResult> ReadAsync(Point clickedAt)
    {
        Reads.Add(clickedAt);
        var read = new TaskCompletionSource<SelectedTextResult>();
        PendingReads.Add(read);
        return read.Task;
    }

    public EditBoxKind KindOf(nint window) => Kinds.GetValueOrDefault(window, EditBoxKind.None);

    public Task<EditBoxResult> InsertAtAsync(nint box, Point point, string text)
    {
        Steps.Add($"insert {box} at {point.X},{point.Y}: {text}");
        return Task.FromResult(InsertResult);
    }

    public Task<bool> PlaceCursorAsync(Point point)
    {
        Steps.Add($"place at {point.X},{point.Y}");
        return Task.FromResult(Placed);
    }

    public Task<EditBoxResult> InsertAtCursorAsync(nint box, string text)
    {
        Steps.Add($"insert {box} at the cursor: {text}");
        return Task.FromResult(CursorInsertResult);
    }

    public Task<bool> FocusAsync(nint root, Point point)
    {
        Steps.Add($"focus {root} at {point.X},{point.Y}");
        return Task.FromResult(true);
    }

    public Task<TextDropResult> DropAsync(Point point, string text)
    {
        Steps.Add($"drop at {point.X},{point.Y}: {text}");
        return Task.FromResult(DropResult);
    }

    // Answers the last read.
    public void GiveRead(SelectedTextStatus status, string? text = null) => PendingReads[^1].SetResult(new SelectedTextResult(status, text));

    public AppOfWindow? AppOf(nint root) => Apps.GetValueOrDefault(root) ?? new AppOfWindow(null, null, $@"C:\Apps\{root}.exe", (uint)root);

    public bool Exists(nint window) => true;

    // Answers the last question.
    public void Answer(TextBoxStatus status, nint window = 0) => Questions[^1].SetResult(new TextBoxAnswer(status, window));
}
