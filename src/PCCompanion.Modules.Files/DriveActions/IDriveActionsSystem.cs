using System;
using System.Collections.Generic;
using System.Threading;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>
/// What Drive Actions reads and changes in Windows, so that tests can use fake drives and files.
/// Everything here runs on the feature's thread and may wait for the disk, except
/// <see cref="WatchDevices"/>, which runs on the host's UI thread.
/// </summary>
public interface IDriveActionsSystem
{
    /// <summary>Whether the host runs as administrator after a UAC prompt, while Drive Actions pauses.</summary>
    bool IsUacElevated { get; }

    /// <summary>The letters of the drives Windows has now, as a mask: bit 0 for A, bit 25 for Z.</summary>
    uint Letters { get; }

    /// <summary>Runs the current thread, the feature's, at background priority.</summary>
    void EnterBackgroundMode();

    /// <summary>The drive with <paramref name="letter"/>, or <see langword="null"/> when there's none.</summary>
    DriveReading? ReadDrive(char letter);

    /// <summary>
    /// The text of the ID file at the drive's root, or <see langword="null"/> when there's none, it's
    /// a link, a folder or too large to be one, or it can't be read.
    /// </summary>
    string? ReadIdFile(string root);

    /// <summary>A file or folder's details, without opening its contents, or <see langword="null"/> when it's gone.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused to read it.</exception>
    FileDetails? Read(string path);

    /// <summary>
    /// The files in a folder and its subfolders, with their paths in it, leaving out links and
    /// hidden or system folders and what's in them.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The folder can't be read.</exception>
    IReadOnlyList<ListedFile> List(string folder);

    /// <summary>Creates a folder in a folder that exists. Returns 0, or Windows' error code.</summary>
    int CreateFolder(string path);

    /// <summary>Copies a file without replacing one. Returns 0, or Windows' error code.</summary>
    int Copy(string source, string destination, CancellationToken cancel);

    /// <summary>Renames a file on its drive without replacing one. Returns 0, or Windows' error code.</summary>
    int Rename(string source, string destination);

    /// <summary>Puts a new copy in a file's place in one step. Returns 0, or Windows' error code.</summary>
    int Replace(string newCopy, string file);

    /// <summary>Deletes a file for good, as PC Companion does only with its own unfinished copies. Returns 0, or Windows' error code.</summary>
    int Delete(string path);

    /// <summary>
    /// Writes the ID file at the drive's root, with <paramref name="id"/>, hidden and system,
    /// replacing one that holds no ID. Returns 0, or Windows' error code.
    /// </summary>
    int WriteIdFile(string root, string id);

    /// <summary>Starts the Recycle Bin's own thread, for Undo.</summary>
    IRecycleBin CreateRecycleBin();

    /// <summary>Whether a file has a Mark of the Web.</summary>
    bool HasMark(string path);

    /// <summary>The space the user can still use on the drive a folder is on, or -1 when it can't be read.</summary>
    long FreeSpace(string folder);

    /// <summary>Asks Windows to eject the drive with <paramref name="letter"/>, and waits for the answer.</summary>
    EjectResult Eject(char letter);

    /// <summary>
    /// Starts Windows' messages about volumes and the session. Call it, use what it returns and
    /// dispose it on the host's UI thread.
    /// </summary>
    IDriveActionsDevices WatchDevices();
}

/// <summary>
/// Windows' messages about volumes and the session, on the host's UI thread, so that a drive can be
/// ejected while it's recognized, and nothing runs while the PC is locked.
/// </summary>
public interface IDriveActionsDevices : IDisposable
{
    /// <summary>Volumes with these letters arrived.</summary>
    event EventHandler<VolumesEventArgs>? VolumesArrived;

    /// <summary>The volumes with these letters went.</summary>
    event EventHandler<VolumesEventArgs>? VolumesRemoved;

    /// <summary>The session was locked or disconnected.</summary>
    event EventHandler? SessionLocked;

    /// <summary>The session was unlocked or connected again.</summary>
    event EventHandler? SessionUnlocked;

    /// <summary>Runs <paramref name="action"/> on the host's UI thread, from any thread. Returns false when the watch has ended.</summary>
    bool BeginInvoke(Action action);

    /// <summary>
    /// Has Windows tell <paramref name="ejecting"/> when it asks to eject the drive with
    /// <paramref name="letter"/>, and <paramref name="changed"/> what else happens to it, until disposed.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">The volume can't be opened, or Windows refused.</exception>
    IDisposable WatchVolume(char letter, Action ejecting, Action<VolumeChange> changed);
}
