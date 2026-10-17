using System;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The Windows Open dialog. Unlike the WinRT file picker, it also works in a process that runs
/// as administrator.
/// </summary>
public static class OpenDialog
{
    private const int Cancelled = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    /// <summary>
    /// Shows the dialog for one existing file and returns its full path, or <see langword="null"/>
    /// when the user cancels. Call it on the thread of <paramref name="owner"/>; it returns when
    /// the dialog closes.
    /// </summary>
    /// <param name="filterName">The name of the file type, such as "Programs".</param>
    /// <param name="filterPattern">The file pattern, such as <c>*.exe</c>.</param>
    public static unsafe string? PickFile(nint owner, string title, string filterName, string filterPattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(filterName);
        ArgumentException.ThrowIfNullOrEmpty(filterPattern);

        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            fixed (char* text = title)
            {
                dialog.SetTitle(text);
            }

            fixed (char* name = filterName)
            fixed (char* pattern = filterPattern)
            {
                var fileType = new COMDLG_FILTERSPEC { pszName = name, pszSpec = pattern };
                dialog.SetFileTypes(1, &fileType);
            }

            FILEOPENDIALOGOPTIONS options;
            dialog.GetOptions(&options);
            dialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR);

            try
            {
                dialog.Show((HWND)owner);
            }
            catch (COMException exception) when (exception.HResult == Cancelled)
            {
                return null;
            }

            dialog.GetResult(out var item);
            try
            {
                PWSTR path;
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path);
                try
                {
                    return path.ToString();
                }
                finally
                {
                    Marshal.FreeCoTaskMem((nint)path.Value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }
}
