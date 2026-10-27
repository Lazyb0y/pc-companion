using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>A file in Run now's list, with its box, which starts checked.</summary>
/// <param name="What">What the folder's rules would do with it.</param>
/// <param name="Rule">Which rule would.</param>
public sealed partial class RunNowChoice(RunNowFile file, string what, string rule) : ObservableObject
{
    private bool _isChosen = true;

    public RunNowFile File { get; } = file;

    public string Name => File.Name;

    public string What { get; } = what;

    public string Rule { get; } = rule;

    /// <summary>What and which rule, for screen readers, which read the box by the file's name.</summary>
    public string Help => $"{What} {Rule}";

    public bool IsChosen
    {
        get => _isChosen;
        set => SetProperty(ref _isChosen, value);
    }
}

/// <summary>
/// Run now for a folder: the host lists the files there now that the folder's rules would act
/// on, and Run hands it the files chosen, which it checks again before each acts.
/// </summary>
public sealed partial class RunNowViewModel : ObservableObject
{
    private static readonly CompositeFormat TitleFormat = CompositeFormat.Parse(Strings.FolderRulesRunNowTitle);
    private static readonly CompositeFormat RuleFormat = CompositeFormat.Parse(Strings.FolderRulesRunNowRule);
    private static readonly CompositeFormat MoveFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryMove);
    private static readonly CompositeFormat CopyFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryCopy);
    private static readonly CompositeFormat RenameFormat = CompositeFormat.Parse(Strings.FolderRulesSummaryRename);
    private static readonly CompositeFormat StaysFormat = CompositeFormat.Parse(Strings.FolderRulesFileStays);
    private static readonly CompositeFormat ListFailedFormat = CompositeFormat.Parse(Strings.FolderRulesRunNowListFailed);

    private readonly string _folder;
    private string _status = Strings.FolderRulesRunNowLoading;

    /// <param name="folder">The watched folder's path, as the settings have it.</param>
    /// <param name="name">The folder's name on the page.</param>
    public RunNowViewModel(string folder, string name)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(name);

        _folder = folder;
        Title = string.Format(CultureInfo.CurrentCulture, TitleFormat, name);
    }

    public string Title { get; }

    public ObservableCollection<RunNowChoice> Files { get; } = [];

    /// <summary>Why the list is empty, or what went wrong, or an empty string.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    public bool CanRun => Files.Any(file => file.IsChosen);

    /// <summary>Asks the host for the files.</summary>
    public async Task LoadAsync()
    {
        var answer = await FolderRulesHost.AskAsync(new FolderRulesRequest { Kind = FolderRulesRequestKind.RunNowList, Folder = _folder }, FolderRulesHost.ListTimeout).ConfigureAwait(true);
        foreach (var file in answer is { Status: FolderRulesAnswerStatus.Done } ? answer.Files : [])
        {
            var choice = new RunNowChoice(file, What(file), string.Format(CultureInfo.CurrentCulture, RuleFormat, file.Rule));
            choice.PropertyChanged += OnChoiceChanged;
            Files.Add(choice);
        }

        Status = answer switch
        {
            null => Strings.FolderRulesRunNowNotRunning,
            { Status: FolderRulesAnswerStatus.Refused } => Strings.FolderRulesRunNowRefused,
            { Status: FolderRulesAnswerStatus.Failed } => string.Format(CultureInfo.CurrentCulture, ListFailedFormat, answer.ErrorCode),
            _ when Files.Count == 0 => Strings.FolderRulesRunNowNone,
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(CanRun));
    }

    /// <summary>Hands the files chosen to the host. Returns whether it took them; otherwise the status says why.</summary>
    public async Task<bool> RunAsync()
    {
        var chosen = Files.Where(file => file.IsChosen).Select(file => new ChosenFile { Name = file.File.Name, Size = file.File.Size, Modified = file.File.Modified }).ToList();
        var answer = await FolderRulesHost.AskAsync(new FolderRulesRequest { Kind = FolderRulesRequestKind.RunNow, Folder = _folder, Files = chosen }, FolderRulesHost.QuickTimeout).ConfigureAwait(true);
        if (answer is { Status: FolderRulesAnswerStatus.Done })
        {
            return true;
        }

        Status = answer is null ? Strings.FolderRulesRunNowNotRunning : Strings.FolderRulesRunNowRefused;
        return false;
    }

    private static string What(RunNowFile file) => file switch
    {
        { Naming: not NamingProblem.None } => string.Format(CultureInfo.CurrentCulture, StaysFormat, FolderRulesText.Of(file.Naming)),
        { Action: FolderRuleAction.Move } => string.Format(CultureInfo.CurrentCulture, MoveFormat, Path.Join(file.Destination, file.NewName)),
        { Action: FolderRuleAction.Copy } => string.Format(CultureInfo.CurrentCulture, CopyFormat, Path.Join(file.Destination, file.NewName)),
        { Action: FolderRuleAction.Rename } => string.Format(CultureInfo.CurrentCulture, RenameFormat, file.NewName),
        _ => Strings.FolderRulesSummaryRecycleBin,
    };

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(CanRun));
}
