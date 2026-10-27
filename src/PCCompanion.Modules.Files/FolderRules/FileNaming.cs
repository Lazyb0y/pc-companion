using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>Why a file gets no new name or destination from its rule.</summary>
public enum NamingProblem
{
    None,

    /// <summary>The new name is empty for this file, such as <c>{extension}</c> for a file without one.</summary>
    EmptyName,

    /// <summary>
    /// The new name, or a subfolder made from the file, is one Windows reserves, such as
    /// <c>CON</c>, ends with a dot or a space, or is <c>.</c> or <c>..</c>.
    /// </summary>
    InvalidName,

    /// <summary>The new name, or a subfolder made from the file, is longer than 255 characters.</summary>
    NameTooLong,

    /// <summary>The destination made from the file is the rule's own folder.</summary>
    DestinationIsOwnFolder,

    /// <summary>The destination made from the file is in a folder that rules can't move files to.</summary>
    DestinationNotAllowed,
}

/// <summary>A new name or destination made from a file, or why there's none.</summary>
public sealed record Naming(string Value, NamingProblem Problem)
{
    public static Naming Failed(NamingProblem problem) => new(string.Empty, problem);
}

/// <summary>
/// Makes new names and destinations from a rule's patterns and a file, with the parts in braces
/// replaced by the file's own values, and the names to try when one is taken. See "Actions" and
/// "Moving and copying" in docs/features/files/folder-rules.md.
/// </summary>
public static class FileNaming
{
    /// <summary>The longest file or folder name Windows allows.</summary>
    public const int MaximumNameLength = 255;

    /// <summary>The highest number added to a name that's taken.</summary>
    public const int MaximumNumber = 999;

    private static readonly string[] PartNames = ["name", "extension", "year", "month", "day", "date"];

    // Names that Windows keeps for devices, with or without an extension.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00b9", "COM\u00b2", "COM\u00b3",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00b9", "LPT\u00b2", "LPT\u00b3",
    };

    /// <summary>
    /// Whether <paramref name="pattern"/> is a pattern for a name: its parts in braces are known and
    /// closed, and its own text holds only characters a file name can have.
    /// </summary>
    public static bool IsPattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return Parse(pattern) is not null;
    }

    /// <summary>
    /// Whether a pattern for a new name can make a valid name: it's a pattern, and when it has no
    /// parts, its text is a name Windows allows.
    /// </summary>
    public static bool IsNewNamePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var pieces = Parse(pattern);
        return pieces is not null && (pieces.Any(piece => piece.IsPart) || CheckName(pattern) == NamingProblem.None);
    }

    /// <summary>
    /// The new name of <paramref name="file"/>: <paramref name="pattern"/> with the file's values
    /// in place, followed by the file's extension.
    /// </summary>
    /// <param name="zone">The time zone of the dates, the user's.</param>
    public static Naming NewName(string pattern, FileFacts file, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(zone);

        var pieces = Parse(pattern) ?? throw new ArgumentException("The new name isn't a pattern.", nameof(pattern));
        var baseName = Expand(pieces, file, zone, emptyPartsAllowed: true)!;
        if (baseName.Length == 0)
        {
            return Naming.Failed(NamingProblem.EmptyName);
        }

        var name = file.Extension.Length > 0 ? baseName + "." + file.Extension : baseName;
        var problem = CheckName(name);
        return problem == NamingProblem.None ? new Naming(name, NamingProblem.None) : Naming.Failed(problem);
    }

    /// <summary>
    /// The folder <paramref name="file"/> goes to: <paramref name="destination"/> with the file's
    /// values in place of its parts. A subfolder with a part that's empty for the file is left out.
    /// </summary>
    /// <param name="folder">The rule's own folder, which a Move or Copy can't go to.</param>
    public static Naming Destination(string destination, FileFacts file, TimeZoneInfo zone, string folder, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(places);

        var fixedPart = FolderPaths.FixedPart(destination);
        if (FolderPaths.Normalize(fixedPart, out var path) != FolderProblem.None)
        {
            throw new ArgumentException("The destination doesn't start with a full path.", nameof(destination));
        }

        foreach (var segment in Segments(destination[fixedPart.Length..]))
        {
            var pieces = Parse(segment) ?? throw new ArgumentException("A subfolder of the destination isn't a pattern.", nameof(destination));
            if (Expand(pieces, file, zone, emptyPartsAllowed: false) is not { } name)
            {
                continue;
            }

            var problem = CheckName(name);
            if (problem != NamingProblem.None)
            {
                return Naming.Failed(problem);
            }

            path = Path.Join(path, name);
        }

        if (FolderRuleCheck.CheckFolder(path, places) != FolderProblem.None)
        {
            return Naming.Failed(NamingProblem.DestinationNotAllowed);
        }

        return FolderPaths.Normalize(folder, out var source) == FolderProblem.None && string.Equals(path, source, StringComparison.OrdinalIgnoreCase)
            ? Naming.Failed(NamingProblem.DestinationIsOwnFolder)
            : new Naming(path, NamingProblem.None);
    }

    /// <summary>
    /// The subfolders of a destination after the folder it starts with, such as <c>{year}</c> and
    /// <c>{month}</c> for <c>D:\Invoices\{year}\{month}</c>, are patterns, and none is
    /// <c>.</c> or <c>..</c>, which would leave the destination.
    /// </summary>
    public static bool IsDestinationPattern(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return Segments(destination[FolderPaths.FixedPart(destination).Length..])
            .All(segment => segment is not ("." or "..") && Parse(segment) is not null);
    }

    /// <summary>
    /// The names to try, in order, for a file named <paramref name="name"/>: the name itself, then,
    /// to add a number, the name with <c>(2)</c>, <c>(3)</c> and so on before the extension, as
    /// File Explorer adds, such as <c>report (2).pdf</c>, while the name is short enough.
    /// </summary>
    public static IEnumerable<string> Candidates(string name, NameConflict ifExists)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        yield return name;
        if (ifExists != NameConflict.AddNumber)
        {
            yield break;
        }

        var extension = Path.GetExtension(name);
        var baseName = name[..^extension.Length];
        for (var number = 2; number <= MaximumNumber; number++)
        {
            var numbered = string.Create(CultureInfo.InvariantCulture, $"{baseName} ({number}){extension}");
            if (numbered.Length > MaximumNameLength)
            {
                yield break;
            }

            yield return numbered;
        }
    }

    /// <summary>
    /// The first of the <see cref="Candidates"/> in <paramref name="folder"/> that's free, or
    /// <see langword="null"/>. A name that differs from the file's own path,
    /// <paramref name="source"/>, only in case is free, so a rename can change only the case.
    /// </summary>
    public static string? FirstFree(string folder, string name, NameConflict ifExists, Func<string, bool> exists, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(exists);

        return Candidates(name, ifExists)
            .Select(candidate => Path.Join(folder, candidate))
            .FirstOrDefault(path => string.Equals(path, source, StringComparison.OrdinalIgnoreCase) || !exists(path));
    }

    /// <summary>Whether Windows allows <paramref name="name"/> as a file or folder name.</summary>
    public static NamingProblem CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            return NamingProblem.EmptyName;
        }

        if (name.Length > MaximumNameLength)
        {
            return NamingProblem.NameTooLong;
        }

        var stem = name.Split('.')[0].TrimEnd(' ');
        return name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || ReservedNames.Contains(stem)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? NamingProblem.InvalidName
            : NamingProblem.None;
    }

    private static string[] Segments(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    // The pattern's text and parts in order, or null when a brace isn't closed, a part isn't
    // known, or the text holds a character a name can't have.
    private static List<(bool IsPart, string Text)>? Parse(string pattern)
    {
        var pieces = new List<(bool IsPart, string Text)>();
        var position = 0;
        while (position < pattern.Length)
        {
            var open = pattern.IndexOf('{', position);
            var text = open < 0 ? pattern[position..] : pattern[position..open];
            if (text.Contains('}', StringComparison.Ordinal) || text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }

            if (text.Length > 0)
            {
                pieces.Add((false, text));
            }

            if (open < 0)
            {
                break;
            }

            var close = pattern.IndexOf('}', open);
            var part = close < 0 ? null : pattern[(open + 1)..close];
            if (part is null || !PartNames.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            pieces.Add((true, part.ToUpperInvariant()));
            position = close + 1;
        }

        return pieces;
    }

    // The pattern with the file's values in place; null for a part that's empty for the file,
    // unless empty parts are allowed.
    private static string? Expand(List<(bool IsPart, string Text)> pieces, FileFacts file, TimeZoneInfo zone, bool emptyPartsAllowed)
    {
        var modified = TimeZoneInfo.ConvertTime(file.Modified, zone);
        var result = new StringBuilder();
        foreach (var (isPart, text) in pieces)
        {
            var value = !isPart ? text : text switch
            {
                "NAME" => file.BaseName,
                "EXTENSION" => file.Extension,
                "YEAR" => modified.ToString("yyyy", CultureInfo.InvariantCulture),
                "MONTH" => modified.ToString("MM", CultureInfo.InvariantCulture),
                "DAY" => modified.ToString("dd", CultureInfo.InvariantCulture),
                _ => modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };

            if (value.Length == 0 && !emptyPartsAllowed)
            {
                return null;
            }

            result.Append(value);
        }

        return result.ToString();
    }
}
