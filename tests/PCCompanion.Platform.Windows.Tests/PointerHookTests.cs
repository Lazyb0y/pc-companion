using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Xunit;
using static PCCompanion.Platform.Windows.Tests.TestWindows;

namespace PCCompanion.Platform.Windows.Tests;

// Sets a real low-level mouse hook, but claims an empty area, so it lets every event through;
// nothing moves the pointer or clicks.
public sealed class PointerHookTests
{
    private static readonly PointerClaim Nowhere = new(Rectangle.Empty, new HashSet<PointerTrigger> { new(PointerButton.Middle, PointerKeys.None) });

    [Fact]
    public void AClaimSetsTheHookAndTakingItBackRemovesIt()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var hook = new PointerHook(window);
        Assert.False(PointerHook.IsSet);

        hook.Claim(Nowhere);
        Assert.True(WaitUntil(() => PointerHook.IsSet));

        hook.Claim(null);
        Assert.True(WaitUntil(() => !PointerHook.IsSet));
    }

    [Fact]
    public void TheHookStaysSetWhileAnyHookHasAClaim()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var first = new PointerHook(window);
        using var second = new PointerHook(window);

        first.Claim(Nowhere);
        second.Claim(Nowhere);
        Assert.True(WaitUntil(() => PointerHook.IsSet));

        first.Claim(null);
        Thread.Sleep(200);
        Assert.True(PointerHook.IsSet);

        second.Dispose();
        Assert.True(WaitUntil(() => !PointerHook.IsSet));
    }

    [Fact]
    public void AHookCanClaimAgainAfterTheLastOneWasDisposed()
    {
        using var window = new MessageWindow(UniqueClassName());
        using (var first = new PointerHook(window))
        {
            first.Claim(Nowhere);
            Assert.True(WaitUntil(() => PointerHook.IsSet));
        }

        Assert.False(PointerHook.IsSet);

        using var again = new PointerHook(window);
        again.Claim(Nowhere);
        Assert.True(WaitUntil(() => PointerHook.IsSet));
    }

    [Fact]
    public void SettingTheHookAgainKeepsItSet()
    {
        using var window = new MessageWindow(UniqueClassName());
        using var hook = new PointerHook(window);
        hook.Claim(Nowhere);
        Assert.True(WaitUntil(() => PointerHook.IsSet));

        PointerHook.SetAgain();
        Thread.Sleep(200);

        Assert.True(PointerHook.IsSet);
    }

    [Fact]
    public void ADisposedHookRefusesAClaim()
    {
        using var window = new MessageWindow(UniqueClassName());
        var hook = new PointerHook(window);
        hook.Dispose();

        Assert.Throws<ObjectDisposedException>(() => hook.Claim(Nowhere));
    }

    // The hook's thread sets and removes the hook on its own time.
    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }

        return condition();
    }
}
