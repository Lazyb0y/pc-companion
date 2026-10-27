using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>Folder Rules' access to Windows' file system.</summary>
internal sealed class WindowsFolderRulesSystem : IFolderRulesSystem
{
    // The largest Mark of the Web that's read; a mark holds a few lines.
    private const int MaximumMarkLength = 64 * 1024;

    // The watch's buffer, the largest Windows allows for a network folder.
    private const int WatchBufferSize = 64 * 1024;

    public bool IsUacElevated => Elevation.IsUacElevated;

    public void EnterBackgroundMode() => ThreadModes.EnterBackgroundMode();

    public FileDetails? Read(string path) => FileDetails.Read(path);

    public IReadOnlyList<FileDetails> List(string folder) => FileDetails.List(folder);

    public bool CanOpenAlone(string path) => FileDetails.CanOpenAlone(path);

    public MarkOfTheWeb? ReadMark(string path)
    {
        try
        {
            using var stream = new FileStream(path + ":" + MarkOfTheWeb.StreamName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaximumMarkLength];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            // Programs write the mark in UTF-8 or ANSI, which agree for an address, or in UTF-16 after a mark of its own.
            using var reader = new StreamReader(new MemoryStream(buffer, 0, length), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return MarkOfTheWeb.Parse(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
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
}
