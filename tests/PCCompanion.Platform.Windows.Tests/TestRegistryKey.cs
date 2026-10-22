using System;
using Microsoft.Win32;

namespace PCCompanion.Platform.Windows.Tests;

/// <summary>A key of the test's own under HKEY_CURRENT_USER\Software, deleted with everything in it at the end.</summary>
internal sealed class TestRegistryKey : IDisposable
{
    public TestRegistryKey()
    {
        Path = @"Software\PCCompanion.Tests." + Guid.NewGuid().ToString("N");
        Key = Registry.CurrentUser.CreateSubKey(Path);
    }

    /// <summary>The key's path under HKEY_CURRENT_USER.</summary>
    public string Path { get; }

    /// <summary>The key, open for changes.</summary>
    public RegistryKey Key { get; }

    public void Dispose()
    {
        Key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
    }
}
