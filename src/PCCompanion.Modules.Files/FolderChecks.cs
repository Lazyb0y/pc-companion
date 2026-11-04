using System;
using System.Linq;

namespace PCCompanion.Modules.Files;

/// <summary>
/// Checks folders by their paths alone: the folders that features can't use, and the folders
/// where programs keep their own files.
/// </summary>
public static class FolderChecks
{
    /// <summary>Checks a folder to watch, by its path alone.</summary>
    public static FolderProblem CheckFolder(string path, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(places);

        var problem = FolderPaths.Normalize(path, out var fullPath);
        return problem == FolderProblem.None ? CheckPlace(fullPath, places) : problem;
    }

    /// <summary>
    /// Checks a Move or Copy destination by the folder it starts with, before any subfolder made
    /// from the file, such as <c>D:\Invoices</c> for <c>D:\Invoices\{year}</c>.
    /// </summary>
    public static FolderProblem CheckDestination(string destination, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Trim().Length == 0)
        {
            return FolderProblem.NoPath;
        }

        var fixedPart = FolderPaths.FixedPart(destination);
        if (fixedPart.Length == 0)
        {
            return FolderProblem.NotFullPath;
        }

        var problem = CheckFolder(fixedPart, places);
        return problem == FolderProblem.None && !FileNaming.IsDestinationPattern(destination) ? FolderProblem.InvalidPart : problem;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is in <c>AppData</c> or <c>ProgramData</c>, where programs
    /// keep their own files, which the page warns about.
    /// </summary>
    public static bool IsProgramData(string path, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(places);

        return FolderPaths.Normalize(path, out var fullPath) == FolderProblem.None
            && places.ProgramData.Any(folder => FolderPaths.IsInside(fullPath, folder));
    }

    private static FolderProblem CheckPlace(string fullPath, SpecialFolders places)
    {
        if (FolderPaths.IsInside(fullPath, places.DataFolder))
        {
            return FolderProblem.DataFolder;
        }

        if (FolderPaths.IsInside(fullPath, places.Windows))
        {
            return FolderProblem.WindowsFolder;
        }

        return places.ProgramFiles.Any(folder => FolderPaths.IsInside(fullPath, folder)) ? FolderProblem.ProgramFiles : FolderProblem.None;
    }
}
