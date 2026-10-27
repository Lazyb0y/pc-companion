using System;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// What Folder Rules reads and changes in Windows, so that tests can use a fake file system.
/// Everything here runs on the feature's thread and may wait for the disk.
/// </summary>
public interface IFolderRulesSystem
{
    /// <summary>Whether the host runs as administrator after a UAC prompt, while Folder Rules pauses.</summary>
    bool IsUacElevated { get; }

    /// <summary>Runs the current thread, the feature's, at background priority.</summary>
    void EnterBackgroundMode();

    /// <summary>A file or folder's details, without opening its contents, or <see langword="null"/> when it's gone.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused to read it.</exception>
    FileDetails? Read(string path);

    /// <summary>The files and folders directly in a folder.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The folder can't be read.</exception>
    IReadOnlyList<FileDetails> List(string folder);

    /// <summary>Whether no other program has the file open for writing.</summary>
    bool CanOpenAlone(string path);

    /// <summary>The file's Mark of the Web, or <see langword="null"/> when it has none or it can't be read.</summary>
    MarkOfTheWeb? ReadMark(string path);

    /// <summary>Creates a folder in a folder that exists. Returns 0, or Windows' error code.</summary>
    int CreateFolder(string path);

    /// <summary>Moves a file without replacing one. Returns 0, or Windows' error code.</summary>
    int Move(string source, string destination, CancellationToken cancel);

    /// <summary>Copies a file without replacing one. Returns 0, or Windows' error code.</summary>
    int Copy(string source, string destination, CancellationToken cancel);

    /// <summary>
    /// Watches the files directly in a folder until disposed. <paramref name="changed"/> gets the
    /// path of each file that's created, changed or renamed to, and <paramref name="failed"/> is
    /// told when the watch stops, with whether changes were lost; both run on the thread pool.
    /// </summary>
    /// <exception cref="System.ArgumentException">The folder doesn't exist.</exception>
    /// <exception cref="System.IO.IOException">The folder can't be watched.</exception>
    IDisposable Watch(string folder, Action<string> changed, Action<bool> failed);
}
