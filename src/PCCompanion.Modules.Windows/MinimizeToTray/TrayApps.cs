using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Windows.MinimizeToTray;

/// <summary>
/// The windows in the tray, by app, each app with its notification icon's ID and the windows that
/// went last first. Use it on the feature's thread.
/// </summary>
public sealed class TrayApps
{
    // Windows reports a notification icon's ID in 16 bits, and the PC Companion icon and System
    // Meters' use 1 to 4.
    private const uint FirstId = 0x100;
    private const uint IdCount = 0x10000 - FirstId;

    private readonly List<TrayApp> _apps = [];

    public IReadOnlyList<TrayApp> Apps => _apps;

    public int WindowCount => _apps.Sum(app => app.Windows.Count);

    /// <summary>
    /// The icon ID for an app, the same every time for the same app ID or program, so Windows,
    /// which remembers where the user put an icon by its program's path and ID, keeps the app's
    /// icon where the user put it. Two apps can share one, rarely; <see cref="Add"/> then gives the
    /// second the next free ID for the session.
    /// </summary>
    public static uint IdFor(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // FNV-1a of the key in capitals, as app IDs and paths match whatever their case.
        var hash = 2166136261u;
        foreach (var character in key.ToUpperInvariant())
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        return FirstId + (hash % IdCount);
    }

    public TrayApp? AppOf(nint window) => _apps.FirstOrDefault(app => app.Windows.Any(listed => listed.Handle == window));

    public TrayApp? AppWithId(uint id) => _apps.FirstOrDefault(app => app.Id == id);

    public TrayWindow? Find(nint window) => AppOf(window)?.Windows.First(listed => listed.Handle == window);

    public bool HasProcess(uint processId) => _apps.Any(app => app.Windows.Any(window => window.ProcessId == processId));

    /// <summary>Adds the window as its app's newest one, adding the app when it has none in the tray yet.</summary>
    public TrayApp Add(string key, string name, TrayWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var app = _apps.FirstOrDefault(listed => string.Equals(listed.Key, key, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            app = new TrayApp(key, FreeId(key), name);
            _apps.Add(app);
        }

        app.Windows.Insert(0, window);
        return app;
    }

    /// <summary>Removes the window, and its app when it was the app's last one; returns its app.</summary>
    public TrayApp? Remove(nint window)
    {
        var app = AppOf(window);
        if (app is null)
        {
            return null;
        }

        app.Windows.RemoveAll(listed => listed.Handle == window);
        if (app.Windows.Count == 0)
        {
            _apps.Remove(app);
        }

        return app;
    }

    private uint FreeId(string key)
    {
        var id = IdFor(key);
        while (_apps.Any(app => app.Id == id))
        {
            id = FirstId + ((id - FirstId + 1) % IdCount);
        }

        return id;
    }
}

/// <summary>An app with windows in the tray, and what its notification icon shows.</summary>
public sealed class TrayApp(string key, uint id, string name)
{
    public string Key { get; } = key;

    public uint Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>The app's windows in the tray, the one that went last first.</summary>
    public List<TrayWindow> Windows { get; } = [];

    /// <summary>The icon's picture: the newest window's icon, as last read.</summary>
    public uint[]? Picture { get; set; }

    /// <summary>The picture and tooltip last sent to Windows, so an unchanged icon isn't sent again.</summary>
    public (uint[] Picture, string ToolTip)? Shown { get; set; }
}

/// <summary>A window in the tray.</summary>
public sealed class TrayWindow(nint handle, uint processId, string title)
{
    public nint Handle { get; } = handle;

    public uint ProcessId { get; } = processId;

    public string Title { get; set; } = title;
}
