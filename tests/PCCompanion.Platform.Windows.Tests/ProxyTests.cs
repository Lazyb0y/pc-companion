using Microsoft.Win32;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// The policies are read from keys of the test's own, in place of the user's and the computer's.
public sealed class ProxyPoliciesTests
{
    [Fact]
    public void WithoutPoliciesNothingIsRestricted()
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();

        Assert.Equal(ProxyRestrictions.None, ProxyPolicies.Read(user.Key, machine.Key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreventChangingProxySettingsIsReadForTheUserAndTheComputer(bool forUser)
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        Set(forUser ? user.Key : machine.Key, ProxyPolicies.ControlPanelPath, "Proxy", 1);

        Assert.Equal(ProxyRestrictions.PreventChanges, ProxyPolicies.Read(user.Key, machine.Key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreventChangingTheSetupScriptIsReadForTheUserAndTheComputer(bool forUser)
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        Set(forUser ? user.Key : machine.Key, ProxyPolicies.ControlPanelPath, "Autoconfig", 1);

        Assert.Equal(ProxyRestrictions.PreventScriptChanges, ProxyPolicies.Read(user.Key, machine.Key));
    }

    [Fact]
    public void PoliciesSetTo0RestrictNothing()
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        Set(user.Key, ProxyPolicies.ControlPanelPath, "Proxy", 0);
        Set(machine.Key, ProxyPolicies.ControlPanelPath, "Autoconfig", 0);

        Assert.Equal(ProxyRestrictions.None, ProxyPolicies.Read(user.Key, machine.Key));
    }

    [Fact]
    public void PerMachineSettingsAreReadForTheComputerOnly()
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        Set(user.Key, ProxyPolicies.PerMachinePath, "ProxySettingsPerUser", 0);

        Assert.Equal(ProxyRestrictions.None, ProxyPolicies.Read(user.Key, machine.Key));

        Set(machine.Key, ProxyPolicies.PerMachinePath, "ProxySettingsPerUser", 0);

        Assert.Equal(ProxyRestrictions.PerMachine, ProxyPolicies.Read(user.Key, machine.Key));
    }

    [Fact]
    public void PerUserSettingsRestrictNothing()
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        Set(machine.Key, ProxyPolicies.PerMachinePath, "ProxySettingsPerUser", 1);

        Assert.Equal(ProxyRestrictions.None, ProxyPolicies.Read(user.Key, machine.Key));
    }

    [Fact]
    public void SeveralPoliciesAreReadTogether()
    {
        using var user = new TestRegistryKey();
        using var machine = new TestRegistryKey();
        Set(user.Key, ProxyPolicies.ControlPanelPath, "Proxy", 1);
        Set(machine.Key, ProxyPolicies.ControlPanelPath, "Autoconfig", 1);
        Set(machine.Key, ProxyPolicies.PerMachinePath, "ProxySettingsPerUser", 0);

        Assert.Equal(
            ProxyRestrictions.PreventChanges | ProxyRestrictions.PreventScriptChanges | ProxyRestrictions.PerMachine,
            ProxyPolicies.Read(user.Key, machine.Key));
    }

    private static void Set(RegistryKey root, string path, string name, int value)
    {
        using var key = root.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}

// Reading only: writing would change this PC's proxy settings. The Sandbox checks write them.
public sealed class InternetProxyTests
{
    [Fact]
    public void ReadsTheCurrentSettings()
    {
        var settings = InternetProxy.Read();

        Assert.NotNull(settings.Server);
        Assert.NotNull(settings.Bypass);
        Assert.NotNull(settings.ScriptAddress);
    }
}
