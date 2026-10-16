using System.IO;
using System.Linq;
using Xunit;

namespace PCCompanion.Architecture.Tests;

public sealed class DependencyRuleTests
{
    private readonly string _root = RepositoryProjects.FindRoot();

    [Fact]
    public void EveryProjectHasADependencyRule()
    {
        var withoutRule = RepositoryProjects.ProjectFiles(_root)
            .Select(ProjectName)
            .Where(project => !DependencyRules.IsKnown(project))
            .ToList();

        Assert.Empty(withoutRule);
    }

    [Fact]
    public void ProjectReferencesFollowDependencyRules()
    {
        var violations = RepositoryProjects.ProjectFiles(_root)
            .SelectMany(file => RepositoryProjects.ProjectReferences(file)
                .Select(reference => (Project: ProjectName(file), Reference: ProjectName(reference))))
            .Where(pair => !DependencyRules.IsAllowed(pair.Project, pair.Reference))
            .Select(pair => $"{pair.Project} -> {pair.Reference}")
            .ToList();

        Assert.Empty(violations);
    }

    private static string ProjectName(string projectFile) => Path.GetFileNameWithoutExtension(projectFile);
}
