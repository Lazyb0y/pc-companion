using System;
using System.ComponentModel;
using System.IO;
using Windows.Win32;

namespace PCCompanion.Platform.Windows;

/// <summary>Paths relative to the volume they're on, as the Cloud Files API reports them.</summary>
public static class VolumePaths
{
    /// <summary>
    /// The path of <paramref name="path"/> on its volume, from the volume's root, such as
    /// <c>\Users\Ana\Remote Folders\Home</c> for <c>C:\Users\Ana\Remote Folders\Home</c>, or <c>\Home</c>
    /// when the volume is mounted in <c>C:\Users\Ana\Remote Folders</c>.
    /// </summary>
    /// <exception cref="Win32Exception">Windows can't tell the volume.</exception>
    public static unsafe string OnVolume(string path)
    {
        string full = Path.GetFullPath(path);
        Span<char> buffer = stackalloc char[full.Length + 2];
        fixed (char* name = full)
        fixed (char* volume = buffer)
        {
            if (!PInvoke.GetVolumePathName(name, volume, (uint)buffer.Length))
            {
                throw new Win32Exception();
            }
        }

        string mountPoint = buffer[..buffer.IndexOf('\0')].ToString();
        string rest = full.Length > mountPoint.Length ? full[mountPoint.Length..] : string.Empty;
        return "\\" + Path.TrimEndingDirectorySeparator(rest);
    }
}
