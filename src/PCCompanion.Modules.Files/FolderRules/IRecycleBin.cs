using System;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Moves files to the Recycle Bin on a thread of its own, one at a time, so that while Windows
/// asks about a file too big for it, the other rules go on. See <see cref="RecycleBin"/>.
/// </summary>
public interface IRecycleBin : IDisposable
{
    /// <summary>
    /// Queues a file. On the Recycle Bin's thread, <paramref name="asking"/> runs when Windows asks
    /// whether to delete it for good, and <paramref name="done"/> once it's gone or stayed.
    /// </summary>
    void Recycle(string path, Action asking, Action<RecycleResult> done);
}
