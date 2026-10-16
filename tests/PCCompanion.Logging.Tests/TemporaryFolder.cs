using System;
using System.IO;

namespace PCCompanion.Logging.Tests;

/// <summary>
/// A new, empty folder under the temp folder that is deleted when disposed.
/// </summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "PCCompanion.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(FullPath))
        {
            Directory.Delete(FullPath, recursive: true);
        }
    }
}
