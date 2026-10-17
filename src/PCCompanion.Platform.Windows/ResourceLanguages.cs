using System;
using System.Collections.Generic;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// The languages Windows loads this process's language resource files (MUI) in, such as the
/// text built into WinUI controls.
/// </summary>
public static class ResourceLanguages
{
    /// <param name="languageNames">Language names such as <c>ar-SA</c>, in order of preference.</param>
    public static unsafe void SetForProcess(IReadOnlyList<string> languageNames)
    {
        ArgumentNullException.ThrowIfNull(languageNames);

        // A list of null-terminated names, ended by an empty name.
        var buffer = string.Join('\0', languageNames) + "\0\0";
        fixed (char* names = buffer)
        {
            uint count;
            if (!PInvoke.SetProcessPreferredUILanguages(PInvoke.MUI_LANGUAGE_NAME, new PCZZWSTR(names), &count))
            {
                throw new Win32Exception();
            }
        }
    }
}
