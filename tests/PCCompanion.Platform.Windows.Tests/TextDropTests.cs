using System;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// The drag's decisions only; a real drop presses the mouse button, so it's checked in Windows Sandbox.
public sealed class TextDropTests
{
    private static TimeSpan Milliseconds(int value) => TimeSpan.FromMilliseconds(value);

    [Fact]
    public void WhileTheAppHasntOfferedACopyTheButtonStaysDownForHalfASecond()
    {
        Assert.Equal(new DragStep(DragAction.Continue, false), DropDecisions.Next(Milliseconds(100), escape: false, buttonHeld: true, copyOffered: false));
        Assert.Equal(new DragStep(DragAction.Continue, true), DropDecisions.Next(Milliseconds(600), escape: false, buttonHeld: true, copyOffered: false));
    }

    [Fact]
    public void TheTextDropsOnceTheButtonIsUpAfterACopyWasOffered()
    {
        Assert.Equal(new DragStep(DragAction.Continue, false), DropDecisions.Next(Milliseconds(200), escape: false, buttonHeld: true, copyOffered: true));
        Assert.Equal(new DragStep(DragAction.Drop, false), DropDecisions.Next(Milliseconds(250), escape: false, buttonHeld: false, copyOffered: true));
    }

    [Fact]
    public void WithoutACopyTheDragIsCancelled() =>
        Assert.Equal(new DragStep(DragAction.Cancel, false), DropDecisions.Next(Milliseconds(700), escape: false, buttonHeld: false, copyOffered: false));

    [Fact]
    public void AnAnswerAfterASecondCancelsTheDrop() =>
        Assert.Equal(new DragStep(DragAction.Cancel, true), DropDecisions.Next(Milliseconds(1100), escape: false, buttonHeld: false, copyOffered: true));

    [Fact]
    public void EscCancelsTheDrop() =>
        Assert.Equal(new DragStep(DragAction.Cancel, true), DropDecisions.Next(Milliseconds(100), escape: true, buttonHeld: true, copyOffered: true));

    [Fact]
    public void OnlyACopyOfferedWithinASecondCounts()
    {
        Assert.True(DropDecisions.OffersCopy(Milliseconds(300), copy: true));
        Assert.False(DropDecisions.OffersCopy(Milliseconds(300), copy: false));
        Assert.False(DropDecisions.OffersCopy(Milliseconds(1100), copy: true));
    }

    // Starts the drop's thread, with OLE and its windows, and ends it; nothing is pressed or shown.
    [Fact]
    public void TheDropCanStartAndStopAgain()
    {
        var first = new TextDrop();
        first.Dispose();
        using var second = new TextDrop();

        Assert.Throws<ObjectDisposedException>(() => { _ = first.DropAsync(new System.Drawing.Point(0, 0), "text"); });
    }
}
