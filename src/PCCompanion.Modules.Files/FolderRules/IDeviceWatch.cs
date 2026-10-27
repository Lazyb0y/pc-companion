using System;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Windows' messages about drives and power, on the host's UI thread, so that a removable drive
/// can be ejected while one of its folders is watched. See <see cref="Platform.Windows.DeviceNotifications"/>.
/// </summary>
public interface IDeviceWatch : IDisposable
{
    /// <summary>A drive arrived, so a folder that couldn't be watched may be back.</summary>
    event EventHandler? DriveArrived;

    /// <summary>The PC woke from sleep.</summary>
    event EventHandler? Resumed;

    /// <summary>
    /// Runs <paramref name="action"/> on the host's UI thread, from any thread. Returns false when
    /// the watch has ended.
    /// </summary>
    bool BeginInvoke(Action action);

    /// <summary>
    /// Has Windows tell <paramref name="ejecting"/> when it asks to eject the drive that
    /// <paramref name="folder"/> is on, until disposed.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The folder can't be opened, or Windows refused.</exception>
    IDisposable WatchFolder(string folder, Action ejecting);
}
