using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace PCCompanion.Platform.Windows;

/// <summary>The user's folders that .NET's special folders don't name.</summary>
public static class KnownFolders
{
    /// <summary>The user's Downloads folder, wherever it was moved, or <see langword="null"/> when Windows doesn't give it.</summary>
    public static unsafe string? Downloads
    {
        get
        {
            var result = PInvoke.SHGetKnownFolderPath(PInvoke.FOLDERID_Downloads, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out var path);
            using (path)
            {
                return result.Failed ? null : new PWSTR((char*)path.DangerousGetHandle()).ToString();
            }
        }
    }
}
