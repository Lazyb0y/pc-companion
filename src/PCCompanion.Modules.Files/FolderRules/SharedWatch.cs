using System;
using System.Threading;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// A folder's watch that the feature's thread and the host's UI thread can both end: the UI
/// thread ends it at once when Windows asks to eject the folder's drive, while the feature's
/// thread may be busy with a file.
/// </summary>
internal sealed class SharedWatch(IDisposable watch) : IDisposable
{
    private IDisposable? _watch = watch;

    public bool IsDisposed => Volatile.Read(ref _watch) is null;

    public void Dispose() => Interlocked.Exchange(ref _watch, null)?.Dispose();
}
