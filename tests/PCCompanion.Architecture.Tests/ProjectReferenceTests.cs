using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PCCompanion.Architecture.Tests;

public sealed class ProjectReferenceTests
{
    private readonly string _root = RepositoryProjects.FindRoot();

    [Fact]
    public void SolutionIncludesEveryProject()
    {
        var onDisk = RepositoryProjects.ProjectFiles(_root)
            .Select(file => RepositoryProjects.ToRelativePath(_root, file));

        Assert.Equal(onDisk, RepositoryProjects.SolutionProjects(_root));
    }

    [Fact]
    public void ProjectReferencesHaveNoCycles()
    {
        var references = RepositoryProjects.ProjectFiles(_root)
            .ToDictionary(file => file, RepositoryProjects.ProjectReferences, StringComparer.OrdinalIgnoreCase);

        var cycle = FindCycle(references);

        Assert.True(
            cycle is null,
            $"Circular project references: {string.Join(" -> ", (cycle ?? []).Select(file => RepositoryProjects.ToRelativePath(_root, file)))}");
    }

    private static List<string>? FindCycle(Dictionary<string, IReadOnlyList<string>> references)
    {
        var finished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var project in references.Keys)
        {
            if (Visit(project) is { } cycle)
            {
                return cycle;
            }
        }

        return null;

        List<string>? Visit(string project)
        {
            var index = path.FindIndex(item => string.Equals(item, project, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                return [.. path[index..], project];
            }

            if (finished.Contains(project))
            {
                return null;
            }

            path.Add(project);
            foreach (var reference in references.GetValueOrDefault(project) ?? [])
            {
                if (Visit(reference) is { } cycle)
                {
                    return cycle;
                }
            }

            path.RemoveAt(path.Count - 1);
            finished.Add(project);
            return null;
        }
    }
}
