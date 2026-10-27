using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>A file in the rule editor's list: whether the rule would act on it, what it would do, and why.</summary>
/// <param name="Outcome">What the rule would do with the file, or that it isn't included.</param>
/// <param name="Details">Whether each condition that's on matches, with the file's own value.</param>
public sealed record RuleFileItem(string Name, bool IsMatch, string Outcome, string Details)
{
    /// <summary>The whole row, for screen readers.</summary>
    public string AutomationName => $"{Name}, {Outcome}. {Details}";
}

/// <summary>
/// The rule editor's fields, what's wrong with them, and the files in the folder now that the
/// rule would act on as it's edited. A problem shows once its box has something in it; Save is
/// available when the rule has no problem.
/// </summary>
public sealed partial class FolderRuleEditor : ObservableObject
{
    private const int MoveIndex = 0;
    private const int CopyIndex = 1;
    private const int RenameIndex = 2;
    private const int RecycleBinIndex = 3;

    // No action or choice is chosen, for a rule whose own this version doesn't know.
    private const int UnknownIndex = -1;

    // How many files the list shows, those that match first; a folder can hold thousands.
    private const int ShownFiles = 100;

    // How long the list waits after a change before it's worked out again, while the user types.
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

    private static readonly CompositeFormat MatchCountFormat = CompositeFormat.Parse(Strings.FolderRulesFilesMatchCount);
    private static readonly CompositeFormat MatchesFormat = CompositeFormat.Parse(Strings.FolderRulesConditionMatches);
    private static readonly CompositeFormat NoMatchFormat = CompositeFormat.Parse(Strings.FolderRulesConditionNoMatch);
    private static readonly CompositeFormat MoveFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryMove);
    private static readonly CompositeFormat CopyFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryCopy);
    private static readonly CompositeFormat RenameFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryRename);
    private static readonly CompositeFormat StaysFormat = CompositeFormat.Parse(Strings.FolderRulesFileStays);
    private static readonly CompositeFormat SizeFormat = CompositeFormat.Parse(Strings.FolderRulesSizeValue);

    // What every field affects: what's shown below the boxes, and whether Save is available.
    private static readonly string[] DependentProperties =
    [
        nameof(NameProblem), nameof(HasNameProblem), nameof(HasNoCondition),
        nameof(NamesProblem), nameof(HasNamesProblem), nameof(ExtensionsProblem), nameof(HasExtensionsProblem),
        nameof(SizeProblem), nameof(HasSizeProblem), nameof(DateProblem), nameof(HasDateProblem),
        nameof(SitesProblem), nameof(HasSitesProblem),
        nameof(IsMoveOrCopy), nameof(IsRename), nameof(HasName), nameof(IsUnknownAction), nameof(IsUnknownIfExists), nameof(ShowsNoRecycleBin), nameof(NewNameHeader),
        nameof(DestinationProblem), nameof(HasDestinationProblem), nameof(IsDestinationMissing), nameof(CreateProblem), nameof(HasCreateProblem),
        nameof(NewNameProblem), nameof(HasNewNameProblem), nameof(CanSave),
    ];

    private readonly FolderRule _rule;
    private readonly string _folder;
    private readonly SpecialFolders _places;
    private readonly TimeZoneInfo _zone = TimeZoneInfo.Local;

    // The marks read so far, by the file's name: each is read once, off the UI thread.
    private readonly Dictionary<string, MarkOfTheWeb?> _marks = new(StringComparer.OrdinalIgnoreCase);

    private string _name;
    private bool _namesOn;
    private string _names;
    private bool _typeOn;
    private string _extensions;
    private bool _sizeOn;
    private bool _largerThanOn;
    private double _largerThan;
    private int _largerThanUnit;
    private bool _smallerThanOn;
    private double _smallerThan;
    private int _smallerThanUnit;
    private bool _dateOn;
    private int _dateWhich;
    private bool _moreThanOn;
    private double _moreThanDays;
    private bool _lessThanOn;
    private double _lessThanDays;
    private bool _sitesOn;
    private string _sites;
    private int _actionIndex;
    private string _destination;
    private string _newName;
    private int _ifExistsIndex;

    // The destination's folder that was looked for last, and whether it was found.
    private string? _checkedDestination;
    private bool _destinationFound = true;
    private bool _createFailed;

    // The folder's files, once read, or null while they're read or when they can't be.
    private IReadOnlyList<FileDetails>? _files;
    private string _filesStatus = Strings.FolderRulesFilesReading;
    private string _matchCount = string.Empty;
    private int _version;

    /// <param name="rule">The rule to edit, or a template's or an empty one for a new rule.</param>
    /// <param name="folder">The watched folder's path, whose files the list shows.</param>
    public FolderRuleEditor(FolderRule rule, bool isNew, string folder, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(places);

        _rule = rule;
        _folder = folder;
        _places = places;
        IsNew = isNew;
        _name = rule.Name;
        _namesOn = rule.Names.Count > 0;
        _names = string.Join("; ", rule.Names);
        _typeOn = rule.Extensions.Count > 0;
        _extensions = string.Join("; ", rule.Extensions);
        _sizeOn = rule.Size is not null;
        (_largerThanOn, _largerThan, _largerThanUnit) = SizeFields(rule.Size?.LargerThan);
        (_smallerThanOn, _smallerThan, _smallerThanUnit) = SizeFields(rule.Size?.SmallerThan);

        // A condition that's turned on starts with one limit, which the user can change.
        _largerThanOn |= rule.Size is null;
        _dateOn = rule.Date is not null;
        _dateWhich = rule.Date?.Which == FileDate.Created ? 1 : 0;
        _moreThanOn = rule.Date?.MoreThanDaysAgo is not null;
        _moreThanDays = rule.Date?.MoreThanDaysAgo ?? 30;
        _lessThanOn = rule.Date?.LessThanDaysAgo is not null;
        _lessThanDays = rule.Date?.LessThanDaysAgo ?? 7;
        _moreThanOn |= rule.Date is null;
        _sitesOn = rule.DownloadedFrom.Count > 0;
        _sites = string.Join("; ", rule.DownloadedFrom);
        _actionIndex = rule.KnownAction switch
        {
            FolderRuleAction.Move => MoveIndex,
            FolderRuleAction.Copy => CopyIndex,
            FolderRuleAction.Rename => RenameIndex,
            FolderRuleAction.RecycleBin => RecycleBinIndex,
            _ => UnknownIndex,
        };
        _destination = rule.Destination;
        _newName = rule.NewName;
        _ifExistsIndex = rule.KnownIfExists switch
        {
            NameConflict.AddNumber => 0,
            NameConflict.Skip => 1,
            _ => UnknownIndex,
        };

        // A network location and most removable drives have no Recycle Bin.
        HasNoRecycleBin = VolumeDetails.KindOf(folder) is DriveKind.Network or DriveKind.Removable;
        _ = ReadFilesAsync();
        _ = CheckDestinationAsync();
    }

    public bool IsNew { get; }

    public string Title => IsNew ? Strings.FolderRulesAddTitle : Strings.FolderRulesEditTitle;

    public string Name
    {
        get => _name;
        set => Change(ref _name, value);
    }

    public string NameProblem => _name.Length == 0 ? string.Empty : RuleText(FolderRuleCheck.CheckName(_name));

    public bool HasNameProblem => NameProblem.Length > 0;

    /// <summary>Why the rule needs a condition, while none is on.</summary>
    public bool HasNoCondition => !ToRule().HasCondition;

    public bool NamesOn
    {
        get => _namesOn;
        set => Change(ref _namesOn, value);
    }

    public string Names
    {
        get => _names;
        set => Change(ref _names, value);
    }

    public string NamesProblem => _namesOn && _names.Trim().Length > 0 ? RuleText(FolderRuleCheck.CheckNames(Split(_names))) : string.Empty;

    public bool HasNamesProblem => NamesProblem.Length > 0;

    public bool TypeOn
    {
        get => _typeOn;
        set => Change(ref _typeOn, value);
    }

    public string Extensions
    {
        get => _extensions;
        set => Change(ref _extensions, value);
    }

    public string ExtensionsProblem => _typeOn && _extensions.Trim().Length > 0 ? RuleText(FolderRuleCheck.CheckExtensions(Split(_extensions))) : string.Empty;

    public bool HasExtensionsProblem => ExtensionsProblem.Length > 0;

    public bool SizeOn
    {
        get => _sizeOn;
        set => Change(ref _sizeOn, value);
    }

    public bool LargerThanOn
    {
        get => _largerThanOn;
        set => Change(ref _largerThanOn, value);
    }

    /// <summary>The amount, or NaN while its box is empty.</summary>
    public double LargerThan
    {
        get => _largerThan;
        set => Change(ref _largerThan, value);
    }

    /// <summary>0 for KB, 1 for MB, 2 for GB.</summary>
    public int LargerThanUnit
    {
        get => _largerThanUnit;
        set => Change(ref _largerThanUnit, value);
    }

    public bool SmallerThanOn
    {
        get => _smallerThanOn;
        set => Change(ref _smallerThanOn, value);
    }

    public double SmallerThan
    {
        get => _smallerThan;
        set => Change(ref _smallerThan, value);
    }

    public int SmallerThanUnit
    {
        get => _smallerThanUnit;
        set => Change(ref _smallerThanUnit, value);
    }

    public string SizeProblem => _sizeOn ? RuleText(FolderRuleCheck.CheckSize(ToRule().Size)) : string.Empty;

    public bool HasSizeProblem => SizeProblem.Length > 0;

    public bool DateOn
    {
        get => _dateOn;
        set => Change(ref _dateOn, value);
    }

    /// <summary>0 for the date it was last modified, 1 for the date it was created.</summary>
    public int DateWhich
    {
        get => _dateWhich;
        set => Change(ref _dateWhich, value);
    }

    public bool MoreThanOn
    {
        get => _moreThanOn;
        set => Change(ref _moreThanOn, value);
    }

    public double MoreThanDays
    {
        get => _moreThanDays;
        set => Change(ref _moreThanDays, value);
    }

    public bool LessThanOn
    {
        get => _lessThanOn;
        set => Change(ref _lessThanOn, value);
    }

    public double LessThanDays
    {
        get => _lessThanDays;
        set => Change(ref _lessThanDays, value);
    }

    public string DateProblem => _dateOn ? RuleText(FolderRuleCheck.CheckDate(ToRule().Date)) : string.Empty;

    public bool HasDateProblem => DateProblem.Length > 0;

    public bool SitesOn
    {
        get => _sitesOn;
        set => Change(ref _sitesOn, value);
    }

    public string Sites
    {
        get => _sites;
        set => Change(ref _sites, value);
    }

    public string SitesProblem => _sitesOn && _sites.Trim().Length > 0 ? RuleText(FolderRuleCheck.CheckSites(Split(_sites))) : string.Empty;

    public bool HasSitesProblem => SitesProblem.Length > 0;

    /// <summary>0 Move, 1 Copy, 2 Rename, 3 the Recycle Bin, -1 for an action this version doesn't know.</summary>
    public int ActionIndex
    {
        get => _actionIndex;
        set => Change(ref _actionIndex, value);
    }

    public bool IsMoveOrCopy => _actionIndex is MoveIndex or CopyIndex;

    public bool IsRename => _actionIndex == RenameIndex;

    /// <summary>Whether the action gives the file a name, its own or a new one, so a taken name matters.</summary>
    public bool HasName => _actionIndex is MoveIndex or CopyIndex or RenameIndex;

    /// <summary>Whether the rule has an action from a newer version, which it keeps until another is chosen.</summary>
    public bool IsUnknownAction => _actionIndex == UnknownIndex;

    /// <summary>Whether the rule moves files to the Recycle Bin from a folder on a drive that has none.</summary>
    public bool ShowsNoRecycleBin => _actionIndex == RecycleBinIndex && HasNoRecycleBin;

    public string NewNameHeader => IsRename ? Strings.FolderRulesNewNameHeader : Strings.FolderRulesNewNameOptionalHeader;

    public string Destination
    {
        get => _destination;
        set
        {
            if (Change(ref _destination, value))
            {
                _createFailed = false;
                _ = CheckDestinationAsync();
            }
        }
    }

    /// <summary>
    /// Whether the folder the destination starts with doesn't exist, which the rule needs; files
    /// it would move there stay until it does.
    /// </summary>
    public bool IsDestinationMissing => IsMoveOrCopy && !HasDestinationProblem && _checkedDestination == DestinationFolder() && !_destinationFound;

    /// <summary>Why the destination's folder couldn't be created, or an empty string.</summary>
    public string CreateProblem => IsDestinationMissing && _createFailed ? Strings.FolderRulesCreateFolderFailed : string.Empty;

    public bool HasCreateProblem => CreateProblem.Length > 0;

    public string DestinationProblem => IsMoveOrCopy && _destination.Trim().Length > 0 && FolderRuleCheck.Check(ToRule(), _folder, _places) is RuleProblem.DestinationNotAllowed or RuleProblem.DestinationIsOwnFolder
        ? RuleText(FolderRuleCheck.Check(ToRule(), _folder, _places))
        : string.Empty;

    public bool HasDestinationProblem => DestinationProblem.Length > 0;

    public string NewName
    {
        get => _newName;
        set => Change(ref _newName, value);
    }

    public string NewNameProblem => HasName && _newName.Trim().Length > 0 && !FileNaming.IsNewNamePattern(_newName) ? Strings.FolderRulesRuleInvalidNewName : string.Empty;

    public bool HasNewNameProblem => NewNameProblem.Length > 0;

    /// <summary>0 to add a number, 1 to skip, -1 for a choice this version doesn't know.</summary>
    public int IfExistsIndex
    {
        get => _ifExistsIndex;
        set => Change(ref _ifExistsIndex, value);
    }

    /// <summary>Whether the rule has a choice for a taken name from a newer version, which it keeps until another is chosen.</summary>
    public bool IsUnknownIfExists => HasName && _ifExistsIndex == UnknownIndex;

    public bool CanSave => FolderRuleCheck.Check(ToRule(), _folder, _places) == RuleProblem.None;

    /// <summary>The files in the folder now, those the rule would act on first.</summary>
    public ObservableCollection<RuleFileItem> Files { get; } = [];

    /// <summary>Why the list is empty, or an empty string.</summary>
    public string FilesStatus
    {
        get => _filesStatus;
        private set
        {
            if (SetProperty(ref _filesStatus, value))
            {
                OnPropertyChanged(nameof(HasFilesStatus));
            }
        }
    }

    public bool HasFilesStatus => FilesStatus.Length > 0;

    /// <summary>How many of the folder's files the rule would act on.</summary>
    public string MatchCount
    {
        get => _matchCount;
        private set => SetProperty(ref _matchCount, value);
    }

    /// <summary>Whether the folder's drive has no Recycle Bin.</summary>
    private bool HasNoRecycleBin { get; }

    /// <summary>Creates the folder the destination starts with, off the UI thread.</summary>
    public async Task CreateDestinationAsync()
    {
        if (DestinationFolder() is not { } folder)
        {
            return;
        }

        try
        {
            await Task.Run(() => Directory.CreateDirectory(folder)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _createFailed = true;
        }

        await CheckDestinationAsync().ConfigureAwait(true);
    }

    /// <summary>Adds a kind's extensions to the Type condition's, those it doesn't have yet, and turns it on.</summary>
    public void AddKind(FileKind kind)
    {
        var extensions = Split(_extensions).ToList();
        extensions.AddRange(FileKinds.Extensions(kind).Where(extension => !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)));
        Extensions = string.Join("; ", extensions);
        TypeOn = true;
    }

    /// <summary>
    /// The rule as edited. Conditions that are off are left out, and the action and taken-name
    /// choice of a rule from a newer version are kept until others are chosen.
    /// </summary>
    public FolderRule ToRule() => new()
    {
        Name = _name.Trim(),
        Enabled = _rule.Enabled,
        Names = _namesOn ? Split(_names) : [],
        Extensions = _typeOn ? Split(_extensions) : [],
        Size = _sizeOn
            ? new SizeCondition
            {
                LargerThan = _largerThanOn ? new SizeLimit { Amount = Whole(_largerThan), Unit = Unit(_largerThanUnit) } : null,
                SmallerThan = _smallerThanOn ? new SizeLimit { Amount = Whole(_smallerThan), Unit = Unit(_smallerThanUnit) } : null,
            }
            : null,
        Date = _dateOn
            ? new DateCondition
            {
                Which = _dateWhich == 1 ? FileDate.Created : FileDate.Modified,
                MoreThanDaysAgo = _moreThanOn ? (int)Whole(_moreThanDays) : null,
                LessThanDaysAgo = _lessThanOn ? (int)Whole(_lessThanDays) : null,
            }
            : null,
        DownloadedFrom = _sitesOn ? Split(_sites) : [],
        Action = _actionIndex switch
        {
            MoveIndex => nameof(FolderRuleAction.Move),
            CopyIndex => nameof(FolderRuleAction.Copy),
            RenameIndex => nameof(FolderRuleAction.Rename),
            RecycleBinIndex => nameof(FolderRuleAction.RecycleBin),
            _ => _rule.Action,
        },
        Destination = _destination.Trim(),
        NewName = _newName.Trim(),
        IfExists = _ifExistsIndex switch
        {
            0 => nameof(NameConflict.AddNumber),
            1 => nameof(NameConflict.Skip),
            _ => _rule.IfExists,
        },
    };

    // The folder the destination starts with, before any subfolder made from the file, or null
    // when it isn't a full path.
    private string? DestinationFolder() =>
        FolderPaths.Normalize(FolderPaths.FixedPart(_destination), out var folder) == FolderProblem.None ? folder : null;

    // Whether the destination's folder exists is read off the UI thread, since one on a network
    // location that's offline can take seconds to answer. A result for a destination that changed
    // meanwhile is dropped.
    private async Task CheckDestinationAsync()
    {
        if (DestinationFolder() is not { } folder)
        {
            return;
        }

        var found = await Task.Run(() => Directory.Exists(folder)).ConfigureAwait(true);
        if (folder == DestinationFolder())
        {
            (_checkedDestination, _destinationFound) = (folder, found);
            OnPropertyChanged(nameof(IsDestinationMissing));
            OnPropertyChanged(nameof(CreateProblem));
            OnPropertyChanged(nameof(HasCreateProblem));
        }
    }

    private static (bool On, double Amount, int Unit) SizeFields(SizeLimit? size) =>
        size is null ? (false, 10, (int)SizeUnit.MB) : (true, size.Amount, (int)size.Unit);

    private static SizeUnit Unit(int index) => Enum.IsDefined((SizeUnit)index) ? (SizeUnit)index : SizeUnit.MB;

    // A box's number, or 0, which the checks refuse, while it's empty or not a whole number.
    private static long Whole(double value) =>
        double.IsFinite(value) && value == Math.Floor(value) && Math.Abs(value) <= int.MaxValue ? (long)value : 0;

    private static string[] Split(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string RuleText(RuleProblem problem) => FolderRulesText.Of(problem, ToRule(), _places);

    // Every field affects what's shown below the boxes, whether Save is available, and the list.
    // Returns whether the field changed.
    private bool Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        foreach (var name in DependentProperties)
        {
            OnPropertyChanged(name);
        }

        _ = RefreshFilesAsync(++_version);
        return true;
    }

    // A network folder can take seconds to list, so it's read off the UI thread, once.
    private async Task ReadFilesAsync()
    {
        try
        {
            var files = await Task.Run(() => FileDetails.List(_folder)).ConfigureAwait(true);
            _files = [.. files.Where(file => !FolderRuleFilter.IsLeftAlone(file) && file.Size > 0).OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Win32Exception)
        {
            FilesStatus = Strings.FolderRulesFilesCantRead;
            return;
        }

        await RefreshFilesAsync(_version, wait: false).ConfigureAwait(true);
    }

    // The list is worked out again a moment after the last change, off the UI thread, where the
    // files' marks are read for Downloaded from. A result that a later change overtook is dropped.
    private async Task RefreshFilesAsync(int version, bool wait = true)
    {
        if (wait)
        {
            await Task.Delay(RefreshDelay).ConfigureAwait(true);
        }

        if (version != _version || _files is not { } files)
        {
            return;
        }

        var rule = ToRule();
        if (FolderRuleCheck.Check(rule, _folder, _places) != RuleProblem.None)
        {
            Show([], files.Count, Strings.FolderRulesFilesFixProblems);
            return;
        }

        var matcher = RuleMatcher.Create(rule, 1);
        var now = DateTimeOffset.UtcNow;
        var items = await Task.Run(() => files.Select(file => Describe(matcher, file, now)).ToList()).ConfigureAwait(true);
        if (version == _version)
        {
            Show(items, files.Count, files.Count == 0 ? Strings.FolderRulesFilesNone : string.Empty);
        }
    }

    private void Show(List<RuleFileItem> items, int total, string status)
    {
        Files.Clear();
        foreach (var item in items.OrderByDescending(item => item.IsMatch).Take(ShownFiles))
        {
            Files.Add(item);
        }

        FilesStatus = status;
        MatchCount = status.Length > 0 && items.Count == 0 ? string.Empty : string.Format(CultureInfo.CurrentCulture, MatchCountFormat, items.Count(item => item.IsMatch), total);
    }

    // Runs on the thread pool, which may read the file's mark.
    private RuleFileItem Describe(RuleMatcher matcher, FileDetails file, DateTimeOffset now)
    {
        var facts = new FileFacts(file.Name, file.Size, file.Created, file.Modified);
        var path = Path.Join(_folder, file.Name);
        MarkOfTheWeb? ReadMark()
        {
            lock (_marks)
            {
                if (!_marks.TryGetValue(file.Name, out var mark))
                {
                    mark = MarkOfTheWeb.Read(path);
                    _marks[file.Name] = mark;
                }

                return mark;
            }
        }

        var results = matcher.Explain(facts, now, ReadMark);
        var isMatch = results.All(result => result.Matches);
        var details = string.Join(" · ", results.Select(result => string.Format(
            CultureInfo.CurrentCulture,
            result.Matches ? MatchesFormat : NoMatchFormat,
            ConditionName(result.Condition),
            ValueOf(result.Condition, matcher.Rule, file, ReadMark))));

        var outcome = !isMatch ? Strings.FolderRulesFileNotIncluded : FolderRuleActions.Plan(_folder, matcher, file, _places, _zone) switch
        {
            null => Strings.FolderRulesFileNotIncluded,
            { Naming: not NamingProblem.None } plan => string.Format(CultureInfo.CurrentCulture, StaysFormat, FolderRulesText.Of(plan.Naming)),
            { Action: FolderRuleAction.Move } plan => string.Format(CultureInfo.CurrentCulture, MoveFormat, Path.Join(plan.Destination, plan.NewName)),
            { Action: FolderRuleAction.Copy } plan => string.Format(CultureInfo.CurrentCulture, CopyFormat, Path.Join(plan.Destination, plan.NewName)),
            { Action: FolderRuleAction.Rename } plan => string.Format(CultureInfo.CurrentCulture, RenameFormat, plan.NewName),
            _ => Strings.FolderRulesSummaryRecycleBin,
        };

        return new RuleFileItem(file.Name, isMatch && outcome != Strings.FolderRulesFileNotIncluded, outcome, details);
    }

    private static string ConditionName(RuleCondition condition) => condition switch
    {
        RuleCondition.Name => Strings.FolderRulesNamesSwitch,
        RuleCondition.Type => Strings.FolderRulesTypeSwitch,
        RuleCondition.Size => Strings.FolderRulesSizeSwitch,
        RuleCondition.Date => Strings.FolderRulesDateSwitch,
        _ => Strings.FolderRulesSitesSwitch,
    };

    // The file's own value that the condition compared.
    private static string ValueOf(RuleCondition condition, FolderRule rule, FileDetails file, Func<MarkOfTheWeb?> readMark) => condition switch
    {
        RuleCondition.Name => file.Name,
        RuleCondition.Type => Path.GetExtension(file.Name) is { Length: > 1 } extension ? extension[1..] : Strings.FolderRulesNoExtension,
        RuleCondition.Size => FormatSize(file.Size),
        RuleCondition.Date => (rule.Date?.Which == FileDate.Created ? file.Created : file.Modified).ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
        _ => readMark()?.Source?.Host is { Length: > 0 } site ? site : Strings.FolderRulesNoSite,
    };

    // As File Explorer counts, with 1 KB as 1,024 bytes, rounded up to whole KB.
    private static string FormatSize(long bytes)
    {
        var (amount, unit) = bytes switch
        {
            < 1024L * 1024 => (Math.Ceiling(bytes / 1024.0).ToString("N0", CultureInfo.CurrentCulture), Strings.FolderRulesUnitKB),
            < 1024L * 1024 * 1024 => ((bytes / (1024.0 * 1024)).ToString("N1", CultureInfo.CurrentCulture), Strings.FolderRulesUnitMB),
            _ => ((bytes / (1024.0 * 1024 * 1024)).ToString("N2", CultureInfo.CurrentCulture), Strings.FolderRulesUnitGB),
        };

        return string.Format(CultureInfo.CurrentCulture, SizeFormat, amount, unit);
    }
}
