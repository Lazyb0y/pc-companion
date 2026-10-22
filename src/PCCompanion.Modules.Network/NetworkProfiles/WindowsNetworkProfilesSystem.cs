using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>Network Profiles' access to Windows, for the feature and its page in the settings app.</summary>
public sealed class WindowsNetworkProfilesSystem : INetworkProfilesSystem
{
    private const string WindowClassName = "PCCompanion.NetworkProfiles";

    // Where WinINet keeps the settings, for the user and, with per-machine settings, for the computer.
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public ProxySettings ReadProxy() => InternetProxy.Read();

    public void WriteProxy(ProxySettings settings) => InternetProxy.Write(settings);

    public ProxyRestrictions ReadRestrictions() => ProxyPolicies.Read();

    // The settings, and the policies, so a policy set or removed shows without waiting for the settings to change.
    public IDisposable Watch(Action changed)
    {
        var watches = new Watches();
        try
        {
            watches.Add(new RegistryWatch(Registry.CurrentUser, InternetSettingsPath, changed));
            watches.Add(new RegistryWatch(Registry.LocalMachine, InternetSettingsPath, changed));
            watches.Add(new RegistryWatch(Registry.LocalMachine, ProxyPolicies.PerMachinePath, changed));
            watches.Add(new RegistryWatch(Registry.CurrentUser, ProxyPolicies.ControlPanelPath, changed));
            watches.Add(new RegistryWatch(Registry.LocalMachine, ProxyPolicies.ControlPanelPath, changed));
            return watches;
        }
        catch
        {
            watches.Dispose();
            throw;
        }
    }

    public IThreadInvoker CreateInvoker() => new WindowInvoker(new MessageWindow(WindowClassName));

    public void RunInBackground(TimeSpan delay, Action action)
    {
        if (delay <= TimeSpan.Zero)
        {
            ThreadPool.QueueUserWorkItem(_ => action());
            return;
        }

        _ = Task.Delay(delay).ContinueWith(_ => action(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private sealed class Watches : IDisposable
    {
        private readonly System.Collections.Generic.List<RegistryWatch> _watches = [];

        public void Add(RegistryWatch watch) => _watches.Add(watch);

        public void Dispose() => _watches.ForEach(watch => watch.Dispose());
    }

    private sealed class WindowInvoker(MessageWindow window) : IThreadInvoker
    {
        public void Post(Action action) => window.BeginInvoke(action);

        public void Dispose() => window.Dispose();
    }
}
