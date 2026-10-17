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

    /// <summary>
    /// Returns the available language that strings come from when the user interface language
    /// is <paramref name="language"/>: the language itself, its nearest parent with strings,
    /// or the source language.
    /// </summary>
    public static CultureInfo Resolve(CultureInfo language)
    {
        ArgumentNullException.ThrowIfNull(language);

        for (var culture = language; !culture.Equals(CultureInfo.InvariantCulture); culture = culture.Parent)
        {
            if (Available.Contains(culture))
            {
                return culture;
            }
        }

        return Source;
    }

    /// <summary>
    /// Returns the language names to load Windows language resource files (MUI) in, which exist
    /// per region: the language's main region, the language itself, then its other regions,
    /// so that a language with resources for any region gets them. At most five names.
    /// </summary>
    public static IReadOnlyList<string> ForWindowsResources(CultureInfo language)
    {
        ArgumentNullException.ThrowIfNull(language);

        var neutral = language.IsNeutralCulture ? language : language.Parent;
        var otherRegions = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(culture => culture.Parent.Equals(neutral))
            .Select(culture => culture.Name)
            .Order(StringComparer.Ordinal);

        return otherRegions
            .Prepend(neutral.Name)
            .Prepend(CultureInfo.CreateSpecificCulture(language.Name).Name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

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
