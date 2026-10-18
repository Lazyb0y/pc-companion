using System;
using System.Threading;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test uses its own lock name.
public sealed class InstanceLockTests
{
    [Fact]
    public void ALockNobodyHoldsIsNotHeld()
    {
        Assert.False(InstanceLock.IsHeld(UniqueName()));
    }

    [Fact]
    public void ALockAProcessHoldsIsHeld()
    {
        var name = UniqueName();
        using var held = new Mutex(initiallyOwned: true, name);

        Assert.True(InstanceLock.IsHeld(name));
    }

    private static string UniqueName() => @"Local\PCCompanion.Tests." + Guid.NewGuid().ToString("N");
}
