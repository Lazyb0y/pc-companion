using System;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Marks only windows the tests create, which stay hidden, with names no other test uses.
public sealed class WindowMarkTests
{
    [Fact]
    public void AMarkedWindowIsFoundUntilTheMarkIsRemoved()
    {
        var mark = new WindowMark(UniqueName());
        using var window = new MessageWindow(UniqueClassName());

        Assert.True(mark.Set(window.Handle));
        Assert.True(mark.IsOn(window.Handle));
        Assert.Contains(window.Handle, mark.FindMarked());

        mark.Remove(window.Handle);

        Assert.False(mark.IsOn(window.Handle));
        Assert.DoesNotContain(window.Handle, mark.FindMarked());
    }

    [Fact]
    public void OnlyMarkedWindowsAreFound()
    {
        var mark = new WindowMark(UniqueName());
        using var marked = new MessageWindow(UniqueClassName());
        using var other = new MessageWindow(UniqueClassName());
        Assert.True(mark.Set(marked.Handle));

        Assert.Equal([marked.Handle], mark.FindMarked());
        Assert.False(mark.IsOn(other.Handle));
    }

    [Fact]
    public void AMarkIsFoundByItsNameAsALaterProcessWouldFindIt()
    {
        var name = UniqueName();
        using var window = new MessageWindow(UniqueClassName());
        Assert.True(new WindowMark(name).Set(window.Handle));

        var later = new WindowMark(name);

        Assert.True(later.IsOn(window.Handle));
        later.Remove(window.Handle);
        Assert.False(new WindowMark(name).IsOn(window.Handle));
    }

    [Fact]
    public void MarksWithOtherNamesAreSeparate()
    {
        var mark = new WindowMark(UniqueName());
        using var window = new MessageWindow(UniqueClassName());
        Assert.True(mark.Set(window.Handle));

        Assert.False(new WindowMark(UniqueName()).IsOn(window.Handle));
    }

    [Fact]
    public void ADestroyedWindowIsNoLongerFound()
    {
        var mark = new WindowMark(UniqueName());
        var window = new MessageWindow(UniqueClassName());
        var handle = window.Handle;
        Assert.True(mark.Set(handle));

        window.Dispose();

        Assert.DoesNotContain(handle, mark.FindMarked());
    }

    private static string UniqueName() => "PCCompanion.Tests.Mark." + Guid.NewGuid().ToString("N");
}
