using System;
using System.Threading;
using Microsoft.Win32;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Each test watches and changes only a key of its own.
public sealed class RegistryWatchTests
{
    // The watch calls back on the thread pool, which can take seconds to get to it while other
    // tests hold its threads, as on a busy build machine. Each wait ends at the callback.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void AChangedValueCallsBack()
    {
        using var test = new TestRegistryKey();
        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path, () => changed.Release());

        test.Key.SetValue("Value", 1);

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EachChangeCallsBack()
    {
        using var test = new TestRegistryKey();
        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path, () => changed.Release());

        test.Key.SetValue("Value", 1);
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
        Drain(changed);

        test.Key.SetValue("Value", 2);
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AChangeInASubkeyCallsBack()
    {
        using var test = new TestRegistryKey();
        using (test.Key.CreateSubKey("Connections"))
        {
        }

        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path, () => changed.Release());

        using (var subkey = test.Key.OpenSubKey("Connections", writable: true)!)
        {
            subkey.SetValue("DefaultConnectionSettings", new byte[] { 1, 2, 3 });
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AKeyThatDoesNotExistYetIsWatchedFromTheNearestKeyAbove()
    {
        using var test = new TestRegistryKey();
        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path + @"\Policies\Proxy", () => changed.Release());

        using (test.Key.CreateSubKey(@"Policies\Proxy"))
        {
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AKeyCreatedLaterIsWatchedItself()
    {
        using var test = new TestRegistryKey();
        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path + @"\Policies\Proxy", () => changed.Release());

        using (test.Key.CreateSubKey("Policies"))
        {
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
        Drain(changed);

        using (test.Key.CreateSubKey(@"Policies\Proxy"))
        {
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
        Drain(changed);

        using (var proxy = test.Key.OpenSubKey(@"Policies\Proxy", writable: true)!)
        {
            proxy.SetValue("Value", 1);
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    // Above a key that doesn't exist yet, only subkeys created or deleted can bring it.
    [Fact]
    public void AChangeElsewhereBelowTheKeyAboveDoesNotCallBack()
    {
        using var test = new TestRegistryKey();
        using (test.Key.CreateSubKey("Other"))
        {
        }

        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path + @"\Policies", () => changed.Release());

        using (var other = test.Key.OpenSubKey("Other", writable: true)!)
        {
            other.SetValue("Value", 1);
        }

        Assert.False(changed.Wait(Quiet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ADeletedKeyIsWatchedFromTheNearestKeyAbove()
    {
        using var test = new TestRegistryKey();
        using (test.Key.CreateSubKey("Policies"))
        {
        }

        using var changed = new SemaphoreSlim(0);
        using var watch = new RegistryWatch(Registry.CurrentUser, test.Path + @"\Policies", () => changed.Release());

        test.Key.DeleteSubKeyTree("Policies");
        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
        Drain(changed);

        using (test.Key.CreateSubKey("Policies"))
        {
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
        Drain(changed);

        using (var policies = test.Key.OpenSubKey("Policies", writable: true)!)
        {
            policies.SetValue("Value", 1);
        }

        Assert.True(changed.Wait(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NothingCallsBackAfterDispose()
    {
        using var test = new TestRegistryKey();
        using var changed = new SemaphoreSlim(0);
        var watch = new RegistryWatch(Registry.CurrentUser, test.Path, () => changed.Release());

        watch.Dispose();
        test.Key.SetValue("Value", 1);

        Assert.False(changed.Wait(Quiet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DisposingTwiceDoesNothing()
    {
        using var test = new TestRegistryKey();
        var watch = new RegistryWatch(Registry.CurrentUser, test.Path, () => { });

        watch.Dispose();
        watch.Dispose();
    }

    // Windows can signal one change more than once.
    private static void Drain(SemaphoreSlim changed)
    {
        while (changed.Wait(Quiet, TestContext.Current.CancellationToken))
        {
        }
    }
}
