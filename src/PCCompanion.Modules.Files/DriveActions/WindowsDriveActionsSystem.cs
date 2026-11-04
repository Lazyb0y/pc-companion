using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>Drive Actions' access to Windows' drives and files.</summary>
internal sealed class WindowsDriveActionsSystem : IDriveActionsSystem
{
    // An ID file holds 32 hexadecimal digits; anything much larger isn't one.
    private const int MaximumIdFileSize = 1024;

    // How deep a listing goes into subfolders, so that a drive made to loop can't hold it.
    private const int MaximumDepth = 64;

    public bool IsUacElevated => Elevation.IsUacElevated;

    public uint Letters => DriveDevices.Letters;

    public void EnterBackgroundMode() => ThreadModes.EnterBackgroundMode();

    public DriveReading? ReadDrive(char letter) => DriveDevices.Read(letter);

    public string? ReadIdFile(string root)
    {
        var path = Path.Join(root, DriveIdentity.IdFileName);
        try
        {
            if (FileDetails.Read(path) is not { IsFolder: false, IsLink: false, Size: <= MaximumIdFileSize })
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return null;
        }
    }

    public FileDetails? Read(string path) => FileDetails.Read(path);

    public IReadOnlyList<ListedFile> List(string folder)
    {
        var files = new List<ListedFile>();
        var folders = new Stack<(string Path, string Relative, int Depth)>();
        folders.Push((folder, string.Empty, 0));
        while (folders.Count > 0)
        {
            var (path, relative, depth) = folders.Pop();
            foreach (var item in FileDetails.List(path))
            {
                var itemPath = relative.Length == 0 ? item.Name : Path.Join(relative, item.Name);
                if (!item.IsFolder)
                {
                    files.Add(new ListedFile(itemPath, item));
                }
                else if (DriveFiles.IsListed(item) && depth < MaximumDepth)
                {
                    folders.Push((Path.Join(path, item.Name), itemPath, depth + 1));
                }
            }
        }

        return files;
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

    public int Copy(string source, string destination, CancellationToken cancel) => FileTransfer.Copy(source, destination, cancel);

    public int Rename(string source, string destination) => FileTransfer.Move(source, destination, CancellationToken.None);

    public int Replace(string newCopy, string file) => FileTransfer.Replace(newCopy, file);

    public int Delete(string path)
    {
        try
        {
            File.Delete(path);
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

    public int WriteIdFile(string root, string id)
    {
        var path = Path.Join(root, DriveIdentity.IdFileName);
        try
        {
            if (FileDetails.Read(path) is { } existing)
            {
                if (existing.IsFolder || existing.IsLink)
                {
                    return 80; // ERROR_FILE_EXISTS: something else has the name.
                }

                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.WriteAllText(path, id);
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
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
        catch (Win32Exception exception)
        {
            return exception.NativeErrorCode;
        }
    }

    public IRecycleBin CreateRecycleBin() => new WindowsRecycleBin();

    public bool HasMark(string path) => MarkOfTheWeb.Read(path) is not null;

    public long FreeSpace(string folder)
    {
        try
        {
            return Path.GetPathRoot(folder) is { Length: > 0 } root && !root.StartsWith(@"\\", StringComparison.Ordinal)
                ? new DriveInfo(root).AvailableFreeSpace
                : -1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return -1;
        }
    }

    public EjectResult Eject(char letter) => DriveDevices.Eject(letter);

    public IDriveActionsDevices WatchDevices() => new WindowsDevices();

    // On the host's UI thread, with a hidden window of its own for Windows' messages.
    private sealed class WindowsDevices : IDriveActionsDevices
    {
        private const string WindowClassName = "PCCompanion.DriveActions.Devices";

        private readonly MessageWindow _window = new(WindowClassName);
        private readonly DeviceNotifications _notifications;
        private readonly SessionMonitor? _session;

        public WindowsDevices()
        {
            _notifications = new DeviceNotifications(_window);
            _notifications.VolumesArrived += (_, e) => VolumesArrived?.Invoke(this, e);
            _notifications.VolumesRemoved += (_, e) => VolumesRemoved?.Invoke(this, e);
            try
            {
                _session = new SessionMonitor(_window);
                _session.Locked += (_, e) => SessionLocked?.Invoke(this, e);
                _session.Unlocked += (_, e) => SessionUnlocked?.Invoke(this, e);
            }
            catch (Win32Exception)
            {
                // Without the session's messages, the PC counts as unlocked.
                _session = null;
            }
        }

        public event EventHandler<VolumesEventArgs>? VolumesArrived;

        public event EventHandler<VolumesEventArgs>? VolumesRemoved;

        public event EventHandler? SessionLocked;

        public event EventHandler? SessionUnlocked;

        public bool BeginInvoke(Action action) => _window.BeginInvoke(action);

        public IDisposable WatchVolume(char letter, Action ejecting, Action<VolumeChange> changed) => _notifications.WatchVolume(letter, ejecting, changed);

        public void Dispose()
        {
            _session?.Dispose();
            _notifications.Dispose();
            _window.Dispose();
        }
    }
}
