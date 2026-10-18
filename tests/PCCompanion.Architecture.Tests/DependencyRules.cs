using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Architecture.Tests;

/// <summary>
/// Which projects each project may reference. Keep in sync with docs/architecture/overview.md.
/// </summary>
internal static class DependencyRules
{
    private const string Core = "PCCompanion.Core";
    private const string Logging = "PCCompanion.Logging";
    private const string Localization = "PCCompanion.Localization";
    private const string PlatformWindows = "PCCompanion.Platform.Windows";
    private const string Infrastructure = "PCCompanion.Infrastructure";
    private const string Elevated = "PCCompanion.Elevated";
    private const string Host = "PCCompanion.Host";
    private const string App = "PCCompanion.App";
    private const string ModulePrefix = "PCCompanion.Modules.";
    private const string TestSuffix = ".Tests";

    private static readonly Dictionary<string, string[]> SharedProjects = new(StringComparer.OrdinalIgnoreCase)
    {
        [Core] = [],
        [Localization] = [],
        [PlatformWindows] = [],
        [Logging] = [Core],
        [Infrastructure] = [Core],
        [Elevated] = [Core, Infrastructure, Logging, PlatformWindows],
    };

    private static readonly string[] ModuleReferences = [Core, PlatformWindows, Localization];

    public static bool IsKnown(string project) =>
        SharedProjects.ContainsKey(project) || IsModule(project) || IsEntryPoint(project) || IsTest(project);

    public static bool IsAllowed(string project, string reference)
    {
        if (IsEntryPoint(reference) || IsTest(reference))
        {
            return false;
        }

        if (IsTest(project))
        {
            return true;
        }

        if (IsEntryPoint(project))
        {
            return IsModule(reference) || SharedProjects.ContainsKey(reference);
        }

        var allowed = IsModule(project) ? ModuleReferences : SharedProjects.GetValueOrDefault(project) ?? [];
        return allowed.Contains(reference, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsEntryPoint(string project) =>
        string.Equals(project, App, StringComparison.OrdinalIgnoreCase)
        || string.Equals(project, Host, StringComparison.OrdinalIgnoreCase);

    private static bool IsModule(string project) => project.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsTest(string project) => project.EndsWith(TestSuffix, StringComparison.OrdinalIgnoreCase);
}
