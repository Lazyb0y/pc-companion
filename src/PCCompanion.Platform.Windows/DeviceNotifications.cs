using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>What Windows reported about a watched volume, besides the request to eject it.</summary>
public enum VolumeChange
{
    /// <summary>A file system was mounted on it, as after it's unlocked or formatted.</summary>
    Mounted,

    /// <summary>Its BitLocker state changed, as when it's unlocked.</summary>
    EncryptionChanged,

    /// <summary>Its file system was dismounted, as when it's formatted.</summary>
    Dismounted,

    /// <summary>The eject Windows asked about didn't happen, so the volume is still there, no longer watched.</summary>
    EjectFailed,

    /// <summary>It's gone, ejected or pulled out, and no longer watched.</summary>
    Removed,
}

/// <summary>The drive letters of the volumes that arrived or went, from <see cref="DeviceNotifications"/>.</summary>
public sealed class VolumesEventArgs(uint letters) : EventArgs
{
    /// <summary>The letters as a mask: bit 0 for A, bit 25 for Z.</summary>
    public uint Letters { get; } = letters;

    /// <summary>The letters, from A to Z.</summary>
    public IReadOnlyList<char> Drives { get; } = [.. Enumerable.Range(0, 26).Where(index => (letters & (1u << index)) != 0).Select(index => (char)('A' + index))];
}

/// <summary>
/// Windows' messages about drives, displays and power, through a <see cref="MessageWindow"/>: the
/// request to eject the drive of a folder or a volume that's watched, a watched volume's changes,
/// drives that arrive or go, displays that arrive or leave, going to sleep and waking from it. A
/// handle open on a removable drive keeps Windows from ejecting it, so each handle closes as soon
/// as Windows asks. Create, use and dispose it on the window's thread.
/// </summary>
public sealed class DeviceNotifications : IDisposable
{
    private const uint DeviceArrival = 0x8000;
    private const uint DeviceQueryRemove = 0x8001;
    private const uint DeviceQueryRemoveFailed = 0x8002;
    private const uint DeviceRemovePending = 0x8003;
    private const uint DeviceRemoveComplete = 0x8004;
    private const uint CustomEvent = 0x8006;
    private const uint Suspend = 0x4;
    private const uint ResumeAutomatic = 0x12;

    // The volume events of ioevent.h that a watched volume reports.
    private static readonly Guid VolumeMounted = new("b5804878-1a96-11d2-8ffd-00a0c9a06d32");
    private static readonly Guid VolumeDismounted = new("d16a55e8-1059-11d2-8ffd-00a0c9a06d32");
    private static readonly Guid VolumeEncryptionChanged = new("062998b2-ee1f-4b6a-b857-e76cbbe9a6da");

    private readonly MessageWindow _window;
    private readonly List<Folder> _folders = [];
    private readonly List<Volume> _volumes = [];
    private readonly List<DisplayWatch> _displayWatches = [];
    private bool _disposed;

    public DeviceNotifications(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        _window.MessageReceived += OnMessageReceived;
    }

    /// <summary>A drive arrived, so folders that couldn't be watched may be back.</summary>
    public event EventHandler? DriveArrived;

    /// <summary>Volumes with these drive letters arrived, as when a drive is connected or a card put in.</summary>
    public event EventHandler<VolumesEventArgs>? VolumesArrived;

    /// <summary>The volumes with these drive letters went: ejected, pulled out, or given another letter.</summary>
    public event EventHandler<VolumesEventArgs>? VolumesRemoved;

    /// <summary>The PC is going to sleep or hibernate.</summary>
    public event EventHandler? Suspending;

    /// <summary>The PC woke from sleep.</summary>
    public event EventHandler? Resumed;

    /// <summary>
    /// Has Windows tell <paramref name="ejecting"/> when it asks to eject the drive that
    /// <paramref name="folder"/> is on, at which point the registration has already closed its
    /// handle. Dispose the registration to stop.
    /// </summary>
    /// <exception cref="Win32Exception">The folder can't be opened, or Windows refused.</exception>
    public unsafe IDisposable WatchFolder(string folder, Action ejecting)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(ejecting);

        var handle = PInvoke.CreateFile(
            folder,
            (uint)FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        var filter = new DEV_BROADCAST_HANDLE
        {
            dbch_size = (uint)sizeof(DEV_BROADCAST_HANDLE),
            dbch_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE,
            dbch_handle = (HANDLE)handle.DangerousGetHandle(),
        };
        var registration = PInvoke.RegisterDeviceNotification((HANDLE)_window.Handle, &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (registration.IsNull)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        var watched = new Folder(this, handle, registration, ejecting);
        _folders.Add(watched);
        return watched;
    }

    /// <summary>
    /// Has Windows tell <paramref name="ejecting"/> when it asks to eject the drive with
    /// <paramref name="letter"/>, at which point the registration has already closed its handle,
    /// and <paramref name="changed"/> what else happens to the volume, through a handle to the
    /// volume opened without access, which a locked BitLocker volume allows too. After
    /// <see cref="VolumeChange.EjectFailed"/> and <see cref="VolumeChange.Removed"/>, the volume
    /// isn't watched any more. Dispose the registration to stop.
    /// </summary>
    /// <exception cref="Win32Exception">The volume can't be opened, or Windows refused.</exception>
    public unsafe IDisposable WatchVolume(char letter, Action ejecting, Action<VolumeChange> changed)
    {
        ArgumentNullException.ThrowIfNull(ejecting);
        ArgumentNullException.ThrowIfNull(changed);
        if (!char.IsAsciiLetter(letter))
        {
            throw new ArgumentOutOfRangeException(nameof(letter), letter, "Not a drive letter.");
        }

        var handle = PInvoke.CreateFile(
            $@"\\.\{char.ToUpperInvariant(letter)}:",
            0,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            0,
            null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        var filter = new DEV_BROADCAST_HANDLE
        {
            dbch_size = (uint)sizeof(DEV_BROADCAST_HANDLE),
            dbch_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE,
            dbch_handle = (HANDLE)handle.DangerousGetHandle(),
        };
        var registration = PInvoke.RegisterDeviceNotification((HANDLE)_window.Handle, &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (registration.IsNull)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        var watched = new Volume(this, handle, registration, ejecting, changed);
        _volumes.Add(watched);
        return watched;
    }

    /// <summary>
    /// Has Windows tell <paramref name="changed"/> when a display is connected or disconnected,
    /// also one that Windows doesn't turn on. Dispose the registration to stop.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused.</exception>
    public unsafe IDisposable WatchDisplays(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        var filter = new DEV_BROADCAST_DEVICEINTERFACE_W
        {
            dbcc_size = (uint)sizeof(DEV_BROADCAST_DEVICEINTERFACE_W),
            dbcc_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = PInvoke.GUID_DEVINTERFACE_MONITOR,
        };
        var registration = PInvoke.RegisterDeviceNotification((HANDLE)_window.Handle, &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (registration.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var watch = new DisplayWatch(this, registration, changed);
        _displayWatches.Add(watch);
        return watch;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.MessageReceived -= OnMessageReceived;
        foreach (var folder in _folders.ToArray())
        {
            folder.Dispose();
        }

        foreach (var volume in _volumes.ToArray())
        {
            volume.Dispose();
        }

        foreach (var watch in _displayWatches.ToArray())
        {
            watch.Dispose();
        }
    }

    private unsafe void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == PInvoke.WM_POWERBROADCAST)
        {
            if (e.WParam == Suspend)
            {
                Suspending?.Invoke(this, EventArgs.Empty);
            }
            else if (e.WParam == ResumeAutomatic)
            {
                Resumed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (e.Message != PInvoke.WM_DEVICECHANGE || e.LParam == 0)
        {
            return;
        }

        var header = (DEV_BROADCAST_HDR*)e.LParam;
        if (header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE && _volumes.Count > 0)
        {
            OnVolumeMessage((uint)e.WParam, (DEV_BROADCAST_HANDLE*)e.LParam);
        }

        switch ((uint)e.WParam)
        {
            case DeviceArrival when header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_VOLUME:
                DriveArrived?.Invoke(this, EventArgs.Empty);
                VolumesArrived?.Invoke(this, new VolumesEventArgs(((DEV_BROADCAST_VOLUME*)e.LParam)->dbcv_unitmask));
                break;
            case DeviceRemoveComplete when header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_VOLUME:
                VolumesRemoved?.Invoke(this, new VolumesEventArgs(((DEV_BROADCAST_VOLUME*)e.LParam)->dbcv_unitmask));
                break;
            case DeviceArrival or DeviceRemoveComplete
                when header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_DEVICEINTERFACE
                    && ((DEV_BROADCAST_DEVICEINTERFACE_W*)e.LParam)->dbcc_classguid == PInvoke.GUID_DEVINTERFACE_MONITOR:
                foreach (var watch in _displayWatches.ToArray())
                {
                    watch.Changed();
                }

                break;
            case DeviceQueryRemove when header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE:
                // Closing the handle at once is what lets Windows eject the drive. Once it's
                // closed, Windows says nothing more about it, also when the eject doesn't happen.
                var notification = ((DEV_BROADCAST_HANDLE*)e.LParam)->dbch_hdevnotify;
                foreach (var folder in _folders.FindAll(folder => folder.Registration == notification))
                {
                    folder.Dispose();
                    folder.Ejecting();
                }

                break;
        }
    }

    // Closing the handle at once lets Windows eject the drive. The registration stays until the
    // eject happens or fails, so that Windows says which, as its documentation shows.
    private unsafe void OnVolumeMessage(uint change, DEV_BROADCAST_HANDLE* message)
    {
        var volume = _volumes.Find(volume => volume.Registration == message->dbch_hdevnotify);
        if (volume is null)
        {
            return;
        }

        switch (change)
        {
            case DeviceQueryRemove:
                volume.CloseHandle();
                volume.Ejecting();
                break;
            case DeviceQueryRemoveFailed:
                volume.Dispose();
                volume.Changed(VolumeChange.EjectFailed);
                break;
            case DeviceRemovePending or DeviceRemoveComplete:
                volume.Dispose();
                volume.Changed(VolumeChange.Removed);
                break;
            case CustomEvent when message->dbch_eventguid == VolumeMounted:
                volume.Changed(VolumeChange.Mounted);
                break;
            case CustomEvent when message->dbch_eventguid == VolumeDismounted:
                volume.Changed(VolumeChange.Dismounted);
                break;
            case CustomEvent when message->dbch_eventguid == VolumeEncryptionChanged:
                volume.Changed(VolumeChange.EncryptionChanged);
                break;
        }
    }

    private sealed class Folder(DeviceNotifications owner, SafeFileHandle handle, HDEVNOTIFY registration, Action ejecting) : IDisposable
    {
        private bool _disposed;

        public HDEVNOTIFY Registration { get; } = registration;

        public Action Ejecting { get; } = ejecting;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PInvoke.UnregisterDeviceNotification(Registration);
            handle.Dispose();
            owner._folders.Remove(this);
        }
    }

    private sealed class Volume(DeviceNotifications owner, SafeFileHandle handle, HDEVNOTIFY registration, Action ejecting, Action<VolumeChange> changed) : IDisposable
    {
        private bool _disposed;

        public HDEVNOTIFY Registration { get; } = registration;

        public Action Ejecting { get; } = ejecting;

        public Action<VolumeChange> Changed { get; } = changed;

        public void CloseHandle() => handle.Dispose();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PInvoke.UnregisterDeviceNotification(Registration);
            handle.Dispose();
            owner._volumes.Remove(this);
        }
    }

    private sealed class DisplayWatch(DeviceNotifications owner, HDEVNOTIFY registration, Action changed) : IDisposable
    {
        private bool _disposed;

        public Action Changed { get; } = changed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PInvoke.UnregisterDeviceNotification(registration);
            owner._displayWatches.Remove(this);
        }
    }
}
