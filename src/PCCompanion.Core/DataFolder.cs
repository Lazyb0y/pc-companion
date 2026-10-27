namespace PCCompanion.Core;

/// <summary>
/// PC Companion's data folder, <c>%LOCALAPPDATA%\PCCompanion</c>, which holds the settings and
/// the files that features keep, such as Folder Rules' history.
/// </summary>
/// <param name="Path">The folder's full path.</param>
public sealed record DataFolder(string Path);
