using PCCompanion.Modules.Input.ShortcutsAndMouseButtons;
using PCCompanion.Platform.Windows;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class ShortcutRecorderTests
{
    private static readonly RecorderKey Control = new(0xA2, 0x1D);
    private static readonly RecorderKey Alt = new(0xA4, 0x38);
    private static readonly RecorderKey Shift = new(0xA0, 0x2A);
    private static readonly RecorderKey Windows = new(0x5B, 0xE05B);
    private static readonly RecorderKey H = new(0x48, 0x23);
    private static readonly RecorderKey E = new(0x45, 0x12);
    private static readonly RecorderKey Tab = new(0x09, 0x0F);
    private static readonly RecorderKey Escape = new(0x1B, 0x01);
    private static readonly RecorderKey F13 = new(0x7C, 0x64);
    private static readonly RecorderKey KeypadOne = new(0x61, 0x4F);

    private readonly ShortcutRecorder _recorder = new();

    [Fact]
    public void ModifiersHeldShowUntilAKeyIsPressed()
    {
        _recorder.KeyDown(Control);
        _recorder.KeyDown(Alt);

        Assert.True(_recorder.IsHolding);
        Assert.Equal(HotKeyModifiers.Control | HotKeyModifiers.Alt, _recorder.Held);
        Assert.Null(_recorder.Recorded);
    }

    [Fact]
    public void AKeyWithModifiersIsKeptByItsPosition()
    {
        Assert.True(_recorder.KeyDown(Control));
        Assert.True(_recorder.KeyDown(Alt));
        Assert.True(_recorder.KeyDown(H));

        Assert.Equal(new RecordedShortcut(HotKeyModifiers.Control | HotKeyModifiers.Alt, "KeyH", Taken: false), _recorder.Recorded);
        Assert.False(_recorder.IsHolding);
    }

    [Fact]
    public void TheShortcutStaysWhenTheKeysAreReleased()
    {
        Press(Control, H);

        Assert.True(_recorder.KeyUp(H));
        Assert.True(_recorder.KeyUp(Control));

        Assert.Equal(new RecordedShortcut(HotKeyModifiers.Control, "KeyH", Taken: false), _recorder.Recorded);
        Assert.Equal(HotKeyModifiers.None, _recorder.Held);
    }

    [Fact]
    public void ModifiersPressedAgainShowWhileHeldAndThenTheShortcutAgain()
    {
        Press(Control, H);
        Release(H, Control);

        _recorder.KeyDown(Shift);
        Assert.True(_recorder.IsHolding);
        _recorder.KeyUp(Shift);

        Assert.False(_recorder.IsHolding);
        Assert.Equal("KeyH", _recorder.Recorded?.Key);
    }

    [Fact]
    public void TabEscEnterAndSpaceWithoutCtrlAltOrTheWindowsKeyKeepTheirMeaning()
    {
        Assert.False(_recorder.KeyDown(Tab));
        Assert.False(_recorder.KeyUp(Tab));
        _recorder.KeyDown(Shift);
        Assert.False(_recorder.KeyDown(Tab));
        Assert.False(_recorder.KeyUp(Tab));
        Assert.False(_recorder.KeyDown(Escape));

        Assert.Null(_recorder.Recorded);
    }

    [Fact]
    public void WithCtrlAltOrTheWindowsKeyTheyAreRecorded()
    {
        _recorder.KeyDown(Alt);

        Assert.True(_recorder.KeyDown(Tab));
        Assert.Equal(new RecordedShortcut(HotKeyModifiers.Alt, "Tab", Taken: false), _recorder.Recorded);
    }

    [Fact]
    public void AKeyAloneIsRecordedForItsProblemToShow()
    {
        Assert.True(_recorder.KeyDown(H));
        Assert.Equal(new RecordedShortcut(HotKeyModifiers.None, "KeyH", Taken: false), _recorder.Recorded);

        _recorder.KeyUp(H);
        _recorder.KeyDown(F13);
        Assert.Equal("F13", _recorder.Recorded?.Key);
    }

    [Fact]
    public void AKeyThatShortcutsCantUseHasNoName()
    {
        _recorder.KeyDown(Control);
        _recorder.KeyDown(KeypadOne);

        Assert.Equal(new RecordedShortcut(HotKeyModifiers.Control, null, Taken: false), _recorder.Recorded);
    }

    [Fact]
    public void AReleaseWithoutItsPressIsAShortcutSomethingElseHas()
    {
        // Windows logo key + E: Windows takes the press and opens File Explorer.
        _recorder.KeyDown(Windows);

        Assert.True(_recorder.KeyUp(E));

        Assert.Equal(new RecordedShortcut(HotKeyModifiers.Windows, "KeyE", Taken: true), _recorder.Recorded);
    }

    [Fact]
    public void AReleaseOfAKeyHeldAsTheBoxGotTheFocusIsIgnored()
    {
        _recorder.Start(HotKeyModifiers.Shift);

        Assert.False(_recorder.KeyUp(Tab));
        Assert.Null(_recorder.Recorded);
        Assert.True(_recorder.IsHolding);
    }

    [Fact]
    public void ARepeatedPressDoesntRecordAgain()
    {
        Press(Control, H);
        var count = 0;
        _recorder.Changed += (_, _) => count++;

        Assert.True(_recorder.KeyDown(H));

        Assert.Equal(0, count);
    }

    [Fact]
    public void ASetShortcutIsShown()
    {
        var chosen = new RecordedShortcut(HotKeyModifiers.Windows, "F13", Taken: false);

        _recorder.Set(chosen);

        Assert.Same(chosen, _recorder.Recorded);
    }

    private void Press(params RecorderKey[] keys)
    {
        foreach (var key in keys)
        {
            _recorder.KeyDown(key);
        }
    }

    private void Release(params RecorderKey[] keys)
    {
        foreach (var key in keys)
        {
            _recorder.KeyUp(key);
        }
    }
}
