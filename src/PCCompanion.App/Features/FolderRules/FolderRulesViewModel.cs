using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>A rule in a folder's list on the page.</summary>
/// <param name="FolderIndex">The folder's place in the list, from 0.</param>
/// <param name="Index">The rule's place in its folder's list, from 0.</param>
/// <param name="Summary">What the rule does, or why it isn't used.</param>
public sealed record RuleItem(
    int FolderIndex,
    int Index,
    string Name,
    string Summary,
    bool IsEnabled,
    string SwitchName,
    string EditName,
    string MoreName,
    bool CanMoveUp,
    bool CanMoveDown)
{
    /// <summary>The folder's and the rule's places, for the rule's buttons.</summary>
    public string Tag => FormattableString.Invariant($"{FolderIndex}:{Index}");
}

/// <summary>A watched folder on the page.</summary>
/// <param name="Index">The folder's place in the list, from 0.</param>
/// <param name="Name">The folder's own name, or the path of a drive's or a share's root.</param>
/// <param name="Problem">Why the folder isn't watched, or a warning about it, or an empty string.</param>
public sealed record FolderItem(
    int Index,
    string Path,
    string Name,
    bool IsEnabled,
    string Problem,
    InfoBarSeverity ProblemSeverity,
    IReadOnlyList<RuleItem> Rules,
    string WatchName,
    string MoreName,
    string AddRuleName,
    bool CanAddRule)
{
    public bool HasProblem => Problem.Length > 0;

    public bool HasNoRules => Rules.Count == 0;
}

/// <summary>
/// The Folder Rules page. Every change is saved right away. Whether each folder can be found is
/// read off the UI thread, since a network folder that can't be reached can take seconds to answer.
/// </summary>
public sealed partial class FolderRulesViewModel : FeatureViewModel<FolderRulesSettings>
{
    private static readonly CompositeFormat WatchFormat = CompositeFormat.Parse(Strings.FolderRulesWatchName);
    private static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.FolderRulesMoreName);
    private static readonly CompositeFormat RuleSwitchFormat = CompositeFormat.Parse(Strings.FolderRulesRuleSwitchName);
    private static readonly CompositeFormat EditFormat = CompositeFormat.Parse(Strings.FolderRulesEditName);
    private static readonly CompositeFormat AddRuleFormat = CompositeFormat.Parse(Strings.FolderRulesAddRuleName);

    private readonly AppSettings _appSettings;

    // What was found at the folders' paths so far.
    private readonly Dictionary<string, FolderState> _found = new(StringComparer.OrdinalIgnoreCase);
    private string _addProblem = string.Empty;

    public FolderRulesViewModel(FeatureSettingsEditor editor, AppDataPaths paths, AppSettings appSettings)
        : base(editor, FolderRulesFeature.Definition)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(appSettings);

        Places = SpecialFolders.ForCurrentUser(paths.Root);
        _appSettings = appSettings;
        RefreshFolders();
    }

    private enum FolderState
    {
        Found,
        NotFound,

        /// <summary>A hidden system folder, or a folder in one, which the host doesn't watch.</summary>
        SystemFolder,
    }

    /// <summary>The folders rules can't use and those that get a warning.</summary>
    public SpecialFolders Places { get; }

    public ObservableCollection<FolderItem> Folders { get; } = [];

    public bool HasNoFolders => Folders.Count == 0;

    public bool CanAddFolder => Settings.Folders.Count < FolderRulesSettings.MaximumFolders;

    /// <summary>Why the folder chosen last wasn't added, or an empty string.</summary>
    public string AddProblem
    {
        get => _addProblem;
        private set
        {
            if (SetProperty(ref _addProblem, value))
            {
                OnPropertyChanged(nameof(HasAddProblem));
            }
        }
    }

    public bool HasAddProblem => AddProblem.Length > 0;

    /// <summary>Whether the host runs as administrator after a UAC prompt, while Folder Rules is paused.</summary>
    public bool IsPaused => HostLauncher.RunsUacElevated(_appSettings.General);

    /// <summary>
    /// Adds a folder to watch, turned on and without rules, or says why it can't be watched, such
    /// as when it's in the list already.
    /// </summary>
    public void AddFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var problem = FolderRuleCheck.CheckFolder(path, Places);
        if (problem == FolderProblem.None && FolderPaths.Normalize(path, out var full) == FolderProblem.None)
        {
            if (Settings.Folders.Any(folder => FolderPaths.Normalize(folder.Path, out var watched) == FolderProblem.None && string.Equals(watched, full, StringComparison.OrdinalIgnoreCase)))
            {
                problem = FolderProblem.AlreadyWatched;
            }
            else if (CanAddFolder)
            {
                SaveFolders([.. Settings.Folders, new WatchedFolder { Path = full }]);
                return;
            }
        }

        AddProblem = FolderRulesText.Of(problem == FolderProblem.None ? FolderProblem.TooMany : problem);
    }

    // A switch saves without building the list again, which would take the focus from it.
    public void SetFolderEnabled(int index, bool enabled) =>
        ChangeFolder(index, folder => folder with { Enabled = enabled }, rebuild: false);

    public void RemoveFolder(int index)
    {
        if (index >= 0 && index < Settings.Folders.Count)
        {
            SaveFolders([.. Settings.Folders.Where((_, at) => at != index)]);
        }
    }

    public void SetRuleEnabled(int folder, int index, bool enabled) =>
        ChangeRules(folder, rules => [.. rules.Select((rule, at) => at == index ? rule with { Enabled = enabled } : rule)], rebuild: false);

    /// <summary>The folder's path, as the settings have it, for the rule editor.</summary>
    public string PathOf(int folder) => Settings.Folders[folder].Path;

    /// <summary>The rule as saved, which a switch may have changed since the list was built.</summary>
    public FolderRule RuleAt(int folder, int index) => Settings.Folders[folder].Rules[index];

    public void AddRule(int folder, FolderRule rule) => ChangeRules(folder, rules => [.. rules, rule]);

    public void ReplaceRule(int folder, int index, FolderRule rule) =>
        ChangeRules(folder, rules => [.. rules.Select((existing, at) => at == index ? rule : existing)]);

    public void DeleteRule(int folder, int index) => ChangeRules(folder, rules => [.. rules.Where((_, at) => at != index)]);

    /// <summary>Moves a rule one place up, with an offset of -1, or down, with 1.</summary>
    public void MoveRule(int folder, int index, int offset) => ChangeRules(folder, rules =>
    {
        var target = index + offset;
        if (index < 0 || target < 0 || index >= rules.Count || target >= rules.Count)
        {
            return rules;
        }

        var moved = rules.ToList();
        (moved[index], moved[target]) = (moved[target], moved[index]);
        return moved;
    });

    /// <summary>The folder's own name, or the path of a drive's or a share's root.</summary>
    public static string NameOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length > 0 ? name : path;
    }

    private void ChangeFolder(int index, Func<WatchedFolder, WatchedFolder> change, bool rebuild = true)
    {
        if (index >= 0 && index < Settings.Folders.Count)
        {
            SaveFolders([.. Settings.Folders.Select((folder, at) => at == index ? change(folder) : folder)], rebuild);
        }
    }

    private void ChangeRules(int folder, Func<IReadOnlyList<FolderRule>, IReadOnlyList<FolderRule>> change, bool rebuild = true) =>
        ChangeFolder(folder, watched => watched with { Rules = change(watched.Rules) }, rebuild);

    // Any change ends the note about the folder that couldn't be added.
    private void SaveFolders(IReadOnlyList<WatchedFolder> folders, bool rebuild = true)
    {
        AddProblem = string.Empty;
        Save(Settings with { Folders = folders });
        if (rebuild)
        {
            RefreshFolders();
        }
    }

    // The list shows at once; what can't be found, or is a hidden system folder, says so once it's
    // been looked at.
    private void RefreshFolders()
    {
        var reviews = FolderRuleCheck.Review(Settings, Places);
        Folders.Clear();
        for (var index = 0; index < Settings.Folders.Count; index++)
        {
            Folders.Add(ToItem(index, Settings.Folders[index], reviews[index]));
        }

        OnPropertyChanged(nameof(HasNoFolders));
        OnPropertyChanged(nameof(CanAddFolder));
        _ = LookAtFoldersAsync();
    }

    private FolderItem ToItem(int index, WatchedFolder folder, FolderReview review)
    {
        var name = NameOf(folder.Path);
        var (problem, severity) = review.Problem != FolderProblem.None
            ? (FolderRulesText.Of(review.Problem), InfoBarSeverity.Error)
            : _found.GetValueOrDefault(folder.Path) switch
            {
                FolderState.SystemFolder => (FolderRulesText.Of(FolderProblem.SystemFolder), InfoBarSeverity.Error),
                FolderState.NotFound => (Strings.FolderRulesFolderNotFound, InfoBarSeverity.Warning),
                _ when FolderRuleCheck.IsProgramData(folder.Path, Places) => (Strings.FolderRulesProgramDataWarning, InfoBarSeverity.Warning),
                _ => (string.Empty, InfoBarSeverity.Informational),
            };

        var rules = folder.Rules.Select((rule, at) =>
        {
            var ruleName = rule.Name.Trim();
            return new RuleItem(
                index,
                at,
                ruleName,
                FolderRulesText.Summary(rule, FolderRulesText.Of(review.Rules[at], rule, Places)),
                rule.Enabled,
                string.Format(CultureInfo.CurrentCulture, RuleSwitchFormat, ruleName),
                string.Format(CultureInfo.CurrentCulture, EditFormat, ruleName),
                string.Format(CultureInfo.CurrentCulture, MoreFormat, ruleName),
                at > 0,
                at < folder.Rules.Count - 1);
        }).ToList();

        return new FolderItem(
            index,
            folder.Path,
            name,
            folder.Enabled,
            problem,
            severity,
            rules,
            string.Format(CultureInfo.CurrentCulture, WatchFormat, name),
            string.Format(CultureInfo.CurrentCulture, MoreFormat, name),
            string.Format(CultureInfo.CurrentCulture, AddRuleFormat, name),
            Settings.Folders.Sum(watched => watched.Rules.Count) < FolderRulesSettings.MaximumRules);
    }

    // Each path is looked at once, off the UI thread.
    private async Task LookAtFoldersAsync()
    {
        var paths = Settings.Folders.Select(folder => folder.Path).Where(path => !_found.ContainsKey(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var found = await Task.Run(() => paths.ToDictionary(path => path, LookAt)).ConfigureAwait(true);
        foreach (var (path, state) in found)
        {
            _found[path] = state;
        }

        if (found.Values.Any(state => state != FolderState.Found))
        {
            var reviews = FolderRuleCheck.Review(Settings, Places);
            for (var index = 0; index < Folders.Count && index < Settings.Folders.Count; index++)
            {
                Folders[index] = ToItem(index, Settings.Folders[index], reviews[index]);
            }
        }
    }

    // As the host does: a folder that's hidden and system, or in one, is one only Windows uses. A
    // drive's root is hidden and system too.
    private static FolderState LookAt(string path)
    {
        if (FolderPaths.Normalize(path, out var full) != FolderProblem.None)
        {
            return FolderState.Found;
        }

        try
        {
            if (!Directory.Exists(full))
            {
                return FolderState.NotFound;
            }

            var root = Path.GetPathRoot(full)!;
            for (var folder = full; folder.Length > root.Length; folder = Path.GetDirectoryName(folder)!)
            {
                if ((File.GetAttributes(folder) & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System))
                {
                    return FolderState.SystemFolder;
                }
            }

            return FolderState.Found;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The host, which reads it too, says in its log why it can't watch it.
            return FolderState.Found;
        }
    }
}
