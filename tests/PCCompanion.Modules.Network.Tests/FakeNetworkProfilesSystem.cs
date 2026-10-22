using System;
using System.Collections.Generic;
using System.ComponentModel;
using PCCompanion.Modules.Network.NetworkProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Network.Tests;

/// <summary>
/// Network Profiles' Windows parts, kept in memory: proxy settings that change as WinINet changes
/// them, policies, and a watch the test signals. Work for the thread pool waits until the test runs
/// it with <see cref="RunBackground"/>, and the invoker runs actions at once, on the test's thread.
/// </summary>
internal sealed class FakeNetworkProfilesSystem : INetworkProfilesSystem
{
    private readonly Queue<Action> _background = new();
    private bool _watching;

    /// <summary>Windows' proxy settings.</summary>
    public ProxySettings Windows { get; set; } = new(AutoDetect: true, UseScript: false, ScriptAddress: "", UseServer: false, Server: "", Bypass: "");

    public ProxyRestrictions Restrictions { get; set; }

    /// <summary>The Windows error that reading the settings fails with, if any.</summary>
    public int? ReadError { get; set; }

    /// <summary>The Windows error that changing the settings fails with, if any.</summary>
    public int? WriteError { get; set; }

    /// <summary>Whether changing the settings reports success and changes nothing, as with per-machine settings.</summary>
    public bool IgnoresWrites { get; set; }

    /// <summary>Thrown when the policies are read, to fail the feature.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Runs while the settings are read, such as a change that signals the watch again.</summary>
    public Action? DuringRead { get; set; }

    public List<ProxySettings> Written { get; } = [];

    public List<TimeSpan> Delays { get; } = [];

    public int Reads { get; private set; }

    public bool IsWatching => _watching;

    public int PendingBackground => _background.Count;

    public int InvokersDisposed { get; private set; }

    private Action? Changed { get; set; }

    public ProxySettings ReadProxy()
    {
        Reads++;
        DuringRead?.Invoke();
        return ReadError is { } error ? throw new Win32Exception(error) : Windows;
    }

    public void WriteProxy(ProxySettings settings)
    {
        if (WriteError is { } error)
        {
            throw new Win32Exception(error);
        }

        Written.Add(settings);
        if (!IgnoresWrites)
        {
            // As WinINet: each part is turned on or off, and only the values of the parts that are on change.
            Windows = new ProxySettings(
                settings.AutoDetect,
                settings.UseScript,
                settings.UseScript ? settings.ScriptAddress : Windows.ScriptAddress,
                settings.UseServer,
                settings.UseServer ? settings.Server : Windows.Server,
                settings.UseServer ? settings.Bypass : Windows.Bypass);
        }
    }

    public ProxyRestrictions ReadRestrictions() => Failure is { } failure ? throw failure : Restrictions;

    public IDisposable Watch(Action changed)
    {
        Changed = changed;
        _watching = true;
        return new Stop(() => _watching = false);
    }

    public IThreadInvoker CreateInvoker() => new Invoker(this);

    public void RunInBackground(TimeSpan delay, Action action)
    {
        Delays.Add(delay);
        _background.Enqueue(action);
    }

    /// <summary>Runs the work waiting for the thread pool, and any it queues.</summary>
    public void RunBackground()
    {
        while (_background.TryDequeue(out var action))
        {
            action();
        }
    }

    /// <summary>Signals the watch, as a change of the settings or their policies does.</summary>
    public void Signal()
    {
        if (_watching)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Changes Windows' settings as another program does, and signals the watch.</summary>
    public void ChangeElsewhere(ProxySettings settings)
    {
        Windows = settings;
        Signal();
    }

    private sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

    private sealed class Invoker(FakeNetworkProfilesSystem system) : IThreadInvoker
    {
        private bool _disposed;

        public void Post(Action action)
        {
            if (!_disposed)
            {
                action();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                system.InvokersDisposed++;
            }
        }
    }
}
