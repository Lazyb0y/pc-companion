using System;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Network.NetworkProfiles;

/// <summary>What Network Profiles uses from Windows; tests use fakes.</summary>
public interface INetworkProfilesSystem
{
    /// <summary>Reads the user's proxy settings. Runs on the thread pool.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused.</exception>
    ProxySettings ReadProxy();

    /// <summary>Changes the user's proxy settings. Runs on the thread pool.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused.</exception>
    void WriteProxy(ProxySettings settings);

    /// <summary>Reads the Group Policy settings that manage the proxy. Runs on the thread pool.</summary>
    ProxyRestrictions ReadRestrictions();

    /// <summary>
    /// Watches the proxy settings and their policies, and calls <paramref name="changed"/> on the
    /// thread pool after a change, until disposed.
    /// </summary>
    IDisposable Watch(Action changed);

    /// <summary>
    /// Creates, on the calling thread, an invoker that runs actions on that thread, from any thread,
    /// until disposed.
    /// </summary>
    IThreadInvoker CreateInvoker();

    /// <summary>Runs <paramref name="action"/> on the thread pool after <paramref name="delay"/>.</summary>
    void RunInBackground(TimeSpan delay, Action action);
}

/// <summary>Runs actions on the thread that created it.</summary>
public interface IThreadInvoker : IDisposable
{
    /// <summary>Queues <paramref name="action"/>, from any thread; it does nothing once the invoker is disposed.</summary>
    void Post(Action action);
}
