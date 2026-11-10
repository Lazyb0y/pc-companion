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

    public SelectedTextApp CheckWindow(nint window) => App;

    public Task<TextBoxAnswer> AskAsync(Point point)
    {
        Asked.Add(point);
        var question = new TaskCompletionSource<TextBoxAnswer>();
        Questions.Add(question);
        return question.Task;
    }

    void IMiddleClickPasteSystem.Claim(PointerClaim? claim) => Claims.Add(claim);

    public AppOfWindow? AppOf(nint root) => Apps.GetValueOrDefault(root) ?? new AppOfWindow(null, null, $@"C:\Apps\{root}.exe", (uint)root);

    public bool Exists(nint window) => true;

    // Answers the last question.
    public void Answer(TextBoxStatus status, nint window = 0) => Questions[^1].SetResult(new TextBoxAnswer(status, window));
}
