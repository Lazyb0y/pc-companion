using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PCCompanion.Architecture.Tests;

/// <summary>
/// Reads the solution and the project files of the repository.
/// </summary>
internal static class RepositoryProjects
{
    public const string SolutionFileName = "PCCompanion.slnx";

    private static readonly string[] ProjectFolders = ["src", "tests"];

    public static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Returns the full paths of all project files under <c>src</c> and <c>tests</c>.
    /// </summary>
    public static IReadOnlyList<string> ProjectFiles(string root) =>
        ProjectFolders
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Returns the project paths listed in the solution, relative to the repository root.
    /// </summary>
    public static IReadOnlyList<string> SolutionProjects(string root) =>
        XDocument.Load(Path.Combine(root, SolutionFileName))
            .Descendants("Project")
            .Select(project => (string?)project.Attribute("Path") ?? string.Empty)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Returns the full paths of the projects that a project file references.
    /// </summary>
    public static IReadOnlyList<string> ProjectReferences(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;

        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)))
            .ToList();
    }

    public static string ToRelativePath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');
}
