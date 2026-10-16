using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;

namespace PCCompanion.Localization;

/// <summary>
/// The user interface languages the app has strings for.
/// </summary>
public static class Languages
{
    /// <summary>
    /// The language of <c>Strings.resx</c>, used when a string has no translation.
    /// </summary>
    public static CultureInfo Source { get; } = CultureInfo.GetCultureInfo(
        typeof(Strings).Assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>()?.CultureName
        ?? throw new InvalidOperationException("The localization assembly has no neutral resources language."));

    /// <summary>
    /// The source language plus every language with a resource folder next to the app,
    /// sorted by language tag. Adding a translation adds a language.
    /// </summary>
    public static IReadOnlyList<CultureInfo> Available { get; } = FindAvailable();

    private static List<CultureInfo> FindAvailable()
    {
        var satelliteFileName = typeof(Strings).Assembly.GetName().Name + ".resources.dll";

        return Directory.EnumerateDirectories(AppContext.BaseDirectory)
            .Where(folder => File.Exists(Path.Combine(folder, satelliteFileName)))
            .Select(folder => CultureInfo.GetCultureInfo(Path.GetFileName(folder)))
            .Append(Source)
            .Order(Comparer<CultureInfo>.Create((x, y) => string.CompareOrdinal(x.Name, y.Name)))
            .ToList();
    }
}
