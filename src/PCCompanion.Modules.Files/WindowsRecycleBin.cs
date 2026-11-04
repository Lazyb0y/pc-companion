using System;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files;

/// <summary>The Recycle Bin, through Platform.Windows' <see cref="RecycleBin"/>.</summary>
internal sealed class WindowsRecycleBin : IRecycleBin
{
    private readonly RecycleBin _bin = new();

    public void Recycle(string path, Action asking, Action<RecycleResult> done) => _bin.Recycle(path, asking, done);

    public void Dispose() => _bin.Dispose();
}
