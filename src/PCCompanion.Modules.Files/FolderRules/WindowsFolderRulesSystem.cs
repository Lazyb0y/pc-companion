using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>Folder Rules' access to Windows' file system.</summary>
internal sealed class WindowsFolderRulesSystem : IFolderRulesSystem
{
    // The watch's buffer, the largest Windows allows for a network folder.
    private const int WatchBufferSize = 64 * 1024;

    public bool IsUacElevated => Elevation.IsUacElevated;

    public void EnterBackgroundMode() => ThreadModes.EnterBackgroundMode();

    public FileDetails? Read(string path) => FileDetails.Read(path);

    public IReadOnlyList<FileDetails> List(string folder) => FileDetails.List(folder);

    public bool CanOpenAlone(string path) => FileDetails.CanOpenAlone(path);

    public MarkOfTheWeb? ReadMark(string path) => MarkOfTheWeb.Read(path);

    // A drive that can't be reached can't take the file either, which the move then reports.
    public bool KeepsStreams(string folder)
    {
        try
        {
            return VolumeDetails.Read(folder).NamedStreams;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    public int CreateFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 5;
        }
        catch (IOException exception)
        {
            return exception.HResult & 0xFFFF;
        }
    }

    public int Move(string source, string destination, CancellationToken cancel) => FileTransfer.Move(source, destination, cancel);

    public int Copy(string source, string destination, CancellationToken cancel) => FileTransfer.Copy(source, destination, cancel);

    public IRecycleBin CreateRecycleBin() => new WindowsRecycleBin();

    public DriveKind KindOf(string folder) => VolumeDetails.KindOf(folder);

    public IDeviceWatch WatchDevices() => new WindowsDeviceWatch();

    public IDisposable Watch(string folder, Action<string> changed, Action<bool> failed)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            InternalBufferSize = WatchBufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Attributes,
        };

        try
        {
            watcher.Created += (_, e) => changed(e.FullPath);
            watcher.Changed += (_, e) => changed(e.FullPath);
            watcher.Renamed += (_, e) => changed(e.FullPath);
            watcher.Error += (_, e) => failed(e.GetException() is InternalBufferOverflowException);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    private sealed class WindowsRecycleBin : IRecycleBin
    {
        private readonly RecycleBin _bin = new();

        public void Recycle(string path, Action asking, Action<RecycleResult> done) => _bin.Recycle(path, asking, done);

        public void Dispose() => _bin.Dispose();
    }

    // On the host's UI thread, with a hidden window of its own for Windows' messages.
    private sealed class WindowsDeviceWatch : IDeviceWatch
    {
        private const string WindowClassName = "PCCompanion.FolderRules.Devices";

        private readonly MessageWindow _window = new(WindowClassName);
        private readonly DeviceNotifications _notifications;

        public WindowsDeviceWatch()
        {
            _notifications = new DeviceNotifications(_window);
            _notifications.DriveArrived += (_, e) => DriveArrived?.Invoke(this, e);
            _notifications.Resumed += (_, e) => Resumed?.Invoke(this, e);
        }

        public event EventHandler? DriveArrived;

        public event EventHandler? Resumed;

        public bool BeginInvoke(Action action) => _window.BeginInvoke(action);

        public IDisposable WatchFolder(string folder, Action ejecting) => _notifications.WatchFolder(folder, ejecting);

        public void Dispose()
        {
            _notifications.Dispose();
            _window.Dispose();
        }
    }
}
