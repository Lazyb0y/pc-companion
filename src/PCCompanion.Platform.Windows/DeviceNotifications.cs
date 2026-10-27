using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Windows' messages about drives, displays and power, through a <see cref="MessageWindow"/>: the
/// request to eject the drive of a folder that's watched, drives that arrive, displays that arrive
/// or leave, going to sleep and waking from it. A handle open on a removable drive keeps Windows
/// from ejecting it, so each folder's handle closes as soon as Windows asks. Create, use and
/// dispose it on the window's thread.
/// </summary>
public sealed class DeviceNotifications : IDisposable
{
    private const uint DeviceArrival = 0x8000;
    private const uint DeviceQueryRemove = 0x8001;
    private const uint DeviceRemoveComplete = 0x8004;
    private const uint Suspend = 0x4;
    private const uint ResumeAutomatic = 0x12;

    private readonly MessageWindow _window;
    private readonly List<Folder> _folders = [];
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
        switch ((uint)e.WParam)
        {
            case DeviceArrival when header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_VOLUME:
                DriveArrived?.Invoke(this, EventArgs.Empty);
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
