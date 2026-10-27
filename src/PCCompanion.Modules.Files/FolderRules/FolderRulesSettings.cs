using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>What a rule does with a file.</summary>
public enum FolderRuleAction
{
    /// <summary>Moves the file to the destination.</summary>
    Move,

    /// <summary>Copies the file to the destination.</summary>
    Copy,

    /// <summary>Gives the file a new name in its folder.</summary>
    Rename,

    /// <summary>Moves the file to the Recycle Bin.</summary>
    RecycleBin,
}

/// <summary>What a rule does when a file with the same name is already there.</summary>
public enum NameConflict
{
    /// <summary>Adds a number to the name, as File Explorer does, such as <c>report (2).pdf</c>.</summary>
    AddNumber,

    /// <summary>Leaves the file where it is.</summary>
    Skip,
}

public enum SizeUnit
{
    KB,
    MB,
    GB,
}

/// <summary>A size, such as 500 KB.</summary>
public sealed record SizeLimit
{
    public long Amount { get; set; }

    public SizeUnit Unit { get; set; } = SizeUnit.MB;
}

/// <summary>The Size condition: larger than, smaller than, or both.</summary>
public sealed record SizeCondition
{
    public SizeLimit? LargerThan { get; set; }

    public SizeLimit? SmallerThan { get; set; }
}

/// <summary>Which of a file's dates the Date condition reads.</summary>
public enum FileDate
{
    Created,
    Modified,
}

/// <summary>
/// The Date condition: the date the file was created or last modified, more than or less than a
/// number of days ago, or both.
/// </summary>
public sealed record DateCondition
{
    public FileDate Which { get; set; } = FileDate.Modified;

    public int? MoreThanDaysAgo { get; set; }

    public int? LessThanDaysAgo { get; set; }
}

/// <summary>
/// A rule: when a file that arrives matches all its conditions, it does its action. A condition
/// is on when it has values. See "Rules" in docs/features/files/folder-rules.md.
/// </summary>
public sealed record FolderRule
{
    /// <summary>The rule's name, shown in the history.</summary>
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>The Name condition: patterns with <c>*</c> and <c>?</c>, of which the file's name must match one.</summary>
    public IReadOnlyList<string> Names { get; set; } = [];

    /// <summary>The Type condition: extensions without the dot, of which the file's must be one.</summary>
    public IReadOnlyList<string> Extensions { get; set; } = [];

    public SizeCondition? Size { get; set; }

    public DateCondition? Date { get; set; }

    /// <summary>The Downloaded from condition: site patterns, such as <c>*.example.com</c>.</summary>
    public IReadOnlyList<string> DownloadedFrom { get; set; } = [];

    /// <summary>
    /// The action's name: <c>Move</c>, <c>Copy</c>, <c>Rename</c> or <c>RecycleBin</c>. It's kept as
    /// written, so a rule with an action this version doesn't know, such as one from a newer
    /// version, keeps it when the settings are saved, and is left out; see <see cref="KnownAction"/>.
    /// </summary>
    public string Action { get; set; } = nameof(FolderRuleAction.Move);

    /// <summary>
    /// For Move and Copy: the folder, which may end in subfolders made from the file, such as
    /// <c>D:\Invoices\{year}</c>.
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// The new name without the extension, such as <c>{date} {name}</c>: for Rename, and optional
    /// for Move and Copy.
    /// </summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>
    /// What Move, Copy and Rename do when a file with the same name is there: <c>AddNumber</c> or
    /// <c>Skip</c>, kept as written like <see cref="Action"/>; see <see cref="KnownIfExists"/>.
    /// </summary>
    public string IfExists { get; set; } = nameof(NameConflict.AddNumber);

    /// <summary>The action, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public FolderRuleAction? KnownAction => Known<FolderRuleAction>(Action);

    /// <summary>What to do when the name is taken, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public NameConflict? KnownIfExists => Known<NameConflict>(IfExists);

    /// <summary>Whether any condition is on.</summary>
    [JsonIgnore]
    public bool HasCondition =>
        Names.Count > 0 || Extensions.Count > 0 || Size is not null || Date is not null || DownloadedFrom.Count > 0;

    // Compares the lists item by item, so the same settings read twice are equal.
    public bool Equals(FolderRule? other) =>
        other is not null && Name == other.Name && Enabled == other.Enabled && Names.SequenceEqual(other.Names)
        && Extensions.SequenceEqual(other.Extensions) && Size == other.Size && Date == other.Date
        && DownloadedFrom.SequenceEqual(other.DownloadedFrom) && Action == other.Action && Destination == other.Destination
        && NewName == other.NewName && IfExists == other.IfExists;

    public override int GetHashCode() => HashCode.Combine(Name, Enabled, Names.Count, Action, Destination, NewName, IfExists);

    // The JSON reader checks nullable annotations on properties but not on a list's items, so a
    // null in the file's lists reads as one.
    internal FolderRule WithoutNulls() => this with
    {
        Names = [.. Names.Where(name => name is not null)],
        Extensions = [.. Extensions.Where(extension => extension is not null)],
        DownloadedFrom = [.. DownloadedFrom.Where(site => site is not null)],
    };

    private static T? Known<T>(string name)
        where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A folder whose arriving files its rules handle, in order.</summary>
public sealed record WatchedFolder
{
    /// <summary>The folder's full path, such as <c>C:\Users\Ana\Downloads</c> or <c>\\server\share\Scans</c>.</summary>
    public string Path { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public IReadOnlyList<FolderRule> Rules { get; set; } = [];

    // Compares the rules one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(WatchedFolder? other) =>
        other is not null && Path == other.Path && Enabled == other.Enabled && Rules.SequenceEqual(other.Rules);

    public override int GetHashCode() => HashCode.Combine(Path, Enabled, Rules.Count);

    internal WatchedFolder WithoutNulls() => this with
    {
        Rules = [.. Rules.Where(rule => rule is not null).Select(rule => rule.WithoutNulls())],
    };
}

/// <summary>
/// Folder Rules settings, stored in <c>features.folderRules</c>. See docs/features/files/folder-rules.md.
/// </summary>
public sealed record FolderRulesSettings : IFeatureSettings<FolderRulesSettings>
{
    /// <summary>The most folders that are watched; later ones are left out.</summary>
    public const int MaximumFolders = 10;

    /// <summary>The most rules that are used, in all folders together; later ones are left out.</summary>
    public const int MaximumRules = 50;

    public bool Enabled { get; set; }

    public IReadOnlyList<WatchedFolder> Folders { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A null in a list,
    /// such as after a hand edit, is left out.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static FolderRulesSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, FolderRulesJsonContext.Default.FolderRulesSettings, () => new FolderRulesSettings());
        return settings with { Folders = [.. settings.Folders.Where(folder => folder is not null).Select(folder => folder.WithoutNulls())] };
    }

    public JsonElement Write() => FeatureSettings.Write(this, FolderRulesJsonContext.Default.FolderRulesSettings);

    public FolderRulesSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the folders one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(FolderRulesSettings? other) =>
        other is not null && Enabled == other.Enabled && Folders.SequenceEqual(other.Folders);

    public override int GetHashCode() => HashCode.Combine(Enabled, Folders.Count);
}
