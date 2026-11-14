using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace PCCompanion.Platform.Windows;

/// <summary>What a shortcut file starts, and the description launchers show under its name.</summary>
/// <param name="Program">The full path of the program it starts.</param>
/// <param name="Arguments">The program's arguments, as one command line.</param>
public sealed record ShortcutContent(string Program, string Arguments, string Description);

/// <summary>
/// Shortcut files (<c>.lnk</c>), such as the entries in Start. Use them on any thread; each call
/// reads or writes one file and waits for nothing else.
/// </summary>
public static class ShellShortcut
{
    // MAX_PATH would cut a longer path; a shortcut stores at most this.
    private const int MaximumPathLength = 1024;

    // INFOTIPSIZE: the longest description and arguments a shortcut stores.
    private const int MaximumTextLength = 1024;

    /// <summary>
    /// Writes a shortcut at <paramref name="path"/>, replacing one that's there, that starts
    /// <paramref name="content"/>'s program in its own folder, with the program's icon and Windows'
    /// mark that keeps it out of Start's Recently added.
    /// </summary>
    /// <exception cref="COMException">Windows couldn't write it.</exception>
    public static unsafe void Write(string path, ShortcutContent content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(content.Program);
            link.SetArguments(content.Arguments);
            link.SetDescription(content.Description);
            link.SetWorkingDirectory(Path.GetDirectoryName(content.Program) ?? string.Empty);
            link.SetIconLocation(content.Program, 0);

            var store = (IPropertyStore)link;
            var key = PInvoke.PKEY_AppUserModel_ExcludeFromShowInNewInstall;
            var value = default(PROPVARIANT);
            value.vt = VARENUM.VT_BOOL;
            value.boolVal = VARIANT_BOOL.VARIANT_TRUE;
            store.SetValue(&key, value);
            store.Commit();

            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// What the shortcut at <paramref name="path"/> starts, or <see langword="null"/> when there's
    /// none or it can't be read.
    /// </summary>
    public static unsafe ShortcutContent? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var link = (IShellLinkW)new ShellLink();
        try
        {
            // STGM_READ.
            ((IPersistFile)link).Load(path, 0);

            Span<char> program = stackalloc char[MaximumPathLength];
            Span<char> text = stackalloc char[MaximumTextLength];

            // SLGP_RAWPATH: the path as written, without expanding environment variables.
            fixed (char* programText = program)
            {
                link.GetPath(programText, program.Length, null, (uint)SLGP_FLAGS.SLGP_RAWPATH);
            }

            var read = new string(program[..program.IndexOf('\0')]);
            fixed (char* arguments = text)
            {
                link.GetArguments(arguments, text.Length);
            }

            var argumentsRead = new string(text[..text.IndexOf('\0')]);
            text.Clear();
            fixed (char* description = text)
            {
                link.GetDescription(description, text.Length);
            }

            return new ShortcutContent(read, argumentsRead, new string(text[..text.IndexOf('\0')]));
        }
        catch (Exception exception) when (exception is COMException or FileNotFoundException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}
