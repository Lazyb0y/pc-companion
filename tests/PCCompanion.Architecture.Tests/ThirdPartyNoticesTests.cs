using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace PCCompanion.Architecture.Tests;

/// <summary>
/// Every package the installer ships needs its license text in ThirdPartyNotices.txt.
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    // The projects whose published files the installer includes.
    private static readonly string[] InstalledProjects = ["src/PCCompanion.App", "src/PCCompanion.Host"];

    // Used only to build; nothing from them is installed.
    private static readonly HashSet<string> BuildOnlyPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Windows.SDK.BuildTools",
        "Microsoft.Windows.SDK.BuildTools.MSIX",
    };

    private readonly string _root = RepositoryProjects.FindRoot();

    [Fact]
    public void NoticesListEveryInstalledPackage()
    {
        var notices = File.ReadAllText(Path.Combine(_root, "ThirdPartyNotices.txt"));

        var missing = InstalledPackages()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(package => !Regex.IsMatch(notices, $@"(?<![\w.]){Regex.Escape(package)}(?![\w.])"))
            .Order(StringComparer.Ordinal);

        Assert.Empty(missing);
    }

    [Fact]
    public void TheSettingsAppInstallsTheNotices()
    {
        var project = File.ReadAllText(Path.Combine(_root, "src/PCCompanion.App/PCCompanion.App.csproj"));

        Assert.Contains(@"Include=""..\..\ThirdPartyNotices.txt""", project, StringComparison.Ordinal);
    }

    private IEnumerable<string> InstalledPackages()
    {
        foreach (var project in InstalledProjects)
        {
            using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, project, "packages.lock.json")));
            foreach (var target in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var package in target.Value.EnumerateObject())
                {
                    var type = package.Value.GetProperty("type").GetString();
                    if (type != "Project" && !BuildOnlyPackages.Contains(package.Name))
                    {
                        yield return package.Name;
                    }
                }
            }
        }
    }
}
