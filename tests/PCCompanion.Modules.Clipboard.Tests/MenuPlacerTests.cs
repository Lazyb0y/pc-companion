using System;
using System.Drawing;
using System.Threading.Tasks;
using PCCompanion.Modules.Clipboard.ClipboardActions;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Clipboard.Tests;

// The fake's pointer is at 10,20; no app is asked and no window is read.
public sealed class MenuPlacerTests
{
    private const nint Window = 42;
    private static readonly Rectangle Line = new(300, 200, 1, 18);
    private static readonly Point Pointer = new(10, 20);

    private readonly FakeClipboardActionsSystem _system = new();

    [Fact]
    public void TheMenuOpensAtWindowsTextCursorWithoutAskingTheApp()
    {
        _system.Front = new ForegroundTextCursor(Window, Line);

        Assert.Equal(new MenuPosition(Line.Location, Line, MenuPlace.AtTextCursor), new MenuPlacer(_system).Find());
        Assert.Empty(_system.Events);
    }

    [Fact]
    public void WithoutWindowsTextCursorTheMenuOpensAtTheAppsTextCursor()
    {
        _system.Front = new ForegroundTextCursor(Window, null);
        _system.AppAnswer = Task.FromResult<Rectangle?>(Line);

        Assert.Equal(new MenuPosition(Line.Location, Line, MenuPlace.AtAppTextCursor), new MenuPlacer(_system).Find());
        Assert.Equal(["ask 42"], _system.Events);
    }

    [Fact]
    public void AnAppWithNoTextCursorGetsTheMenuAtThePointer()
    {
        _system.Front = new ForegroundTextCursor(Window, null);
        _system.AppAnswer = Task.FromResult<Rectangle?>(null);

        Assert.Equal(new MenuPosition(Pointer, null, MenuPlace.AtPointer), new MenuPlacer(_system).Find());
    }

    // An app's object reports errors such as E_NOTIMPL, which .NET turns into exceptions of their own.
    [Fact]
    public void AnAppWhoseAnswerFailsGetsTheMenuAtThePointer()
    {
        _system.Front = new ForegroundTextCursor(Window, null);
        _system.AppAnswer = Task.FromException<Rectangle?>(new NotImplementedException());

        Assert.Equal(new MenuPosition(Pointer, null, MenuPlace.AtPointer), new MenuPlacer(_system).Find());
    }

    [Fact]
    public void WithNoWindowInFrontNoAppIsAsked()
    {
        Assert.Equal(new MenuPosition(Pointer, null, MenuPlace.AtPointer), new MenuPlacer(_system).Find());
        Assert.Empty(_system.Events);
    }

    [Fact]
    public void AnAppAtAnotherIntegrityLevelIsNotAsked()
    {
        _system.Front = new ForegroundTextCursor(Window, null);
        _system.SameIntegrityLevel = false;
        _system.AppAnswer = Task.FromResult<Rectangle?>(Line);

        Assert.Equal(new MenuPosition(Pointer, null, MenuPlace.AtPointer), new MenuPlacer(_system).Find());
        Assert.Empty(_system.Events);
    }

    [Fact]
    public void AnAppThatDoesntAnswerInTimeIsNotAskedAgainUntilItAnswers()
    {
        _system.Front = new ForegroundTextCursor(Window, null);
        var late = new TaskCompletionSource<Rectangle?>();
        _system.AppAnswer = late.Task;
        var placer = new MenuPlacer(_system);

        Assert.Equal(new MenuPosition(Pointer, null, MenuPlace.AtPointerNoAnswer), placer.Find());
        Assert.Equal(new MenuPosition(Pointer, null, MenuPlace.AtPointerStillAsking), placer.Find());
        Assert.Equal(["ask 42"], _system.Events);

        late.SetResult(Line);
        _system.AppAnswer = Task.FromResult<Rectangle?>(Line);

        Assert.Equal(MenuPlace.AtAppTextCursor, placer.Find().Place);
        Assert.Equal(["ask 42", "ask 42"], _system.Events);
    }

    [Fact]
    public void WindowsTextCursorIsUsedWhileAnEarlierRequestStillWaits()
    {
        _system.Front = new ForegroundTextCursor(Window, null);
        var placer = new MenuPlacer(_system);
        Assert.Equal(MenuPlace.AtPointerNoAnswer, placer.Find().Place);

        _system.Front = new ForegroundTextCursor(Window, Line);

        Assert.Equal(new MenuPosition(Line.Location, Line, MenuPlace.AtTextCursor), placer.Find());
        Assert.Equal(["ask 42"], _system.Events);
    }
}
