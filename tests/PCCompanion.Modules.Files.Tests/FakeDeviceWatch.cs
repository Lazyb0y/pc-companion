using System;
using System.Collections.Generic;
using PCCompanion.Modules.Files.FolderRules;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>
/// Windows' messages about drives and power, sent by the test. What the feature posts to the
/// host's UI thread runs at once, on the thread that posts it.
/// </summary>
internal sealed class FakeDeviceWatch(FakeFolderRulesSystem system) : IDeviceWatch
{
    private readonly Dictionary<string, Action> _folders = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? DriveArrived;

    public event EventHandler? Resumed;

    public bool IsDisposed { get; private set; }

    public bool IsWatching(string folder)
    {
        lock (_folders)
        {
            return _folders.ContainsKey(folder);
        }
    }

    public bool BeginInvoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action();
        return true;
    }

    public IDisposable WatchFolder(string folder, Action ejecting)
    {
        lock (_folders)
        {
            _folders[folder] = ejecting;
        }

        system.Note($"register {folder}");
        return new Registration(this, folder);
    }

    /// <summary>Asks to eject the drive of <paramref name="folder"/>, as Windows does.</summary>
    public void Eject(string folder)
    {
        Action? ejecting;
        lock (_folders)
        {
            _folders.Remove(folder, out ejecting);
        }

        ejecting?.Invoke();
    }

    public void Arrive() => DriveArrived?.Invoke(this, EventArgs.Empty);

    public void Resume() => Resumed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => IsDisposed = true;

    private sealed class Registration(FakeDeviceWatch owner, string folder) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._folders)
            {
                owner._folders.Remove(folder);
            }
        }
    }
}
