using System;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// What the feature's thread tells the host's UI thread about the folders on removable drives,
/// whose watches the UI thread ends at once when Windows asks to eject the drive. Called on the
/// feature's thread.
/// </summary>
public interface IDriveTracker
{
    /// <summary>A folder on a removable drive is watched, by <paramref name="watch"/>, which either thread may end.</summary>
    void Watching(string folder, IDisposable watch);

    /// <summary>A folder on a removable drive is no longer watched.</summary>
    void Stopped(string folder);
}
