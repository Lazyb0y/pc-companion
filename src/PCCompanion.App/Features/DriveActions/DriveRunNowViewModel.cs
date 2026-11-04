using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>A file an import would copy, in Run now, with its box.</summary>
/// <param name="Action">The import's number among the drive's actions.</param>
/// <param name="Path">Its path in the folder on the drive.</param>
/// <param name="Help">Where it would go, and whether it was imported before.</param>
public sealed partial class RunNowFile(int action, string path, string help, bool isChosen) : ObservableObject
{
    private bool _isChosen = isChosen;

    public int Action { get; } = action;

    public string Path { get; } = path;

    public string Help { get; } = help;

    public bool IsChosen
    {
        get => _isChosen;
        set => SetProperty(ref _isChosen, value);
    }
}

/// <summary>
/// Run now: what a connected drive's actions would do now, from the host: the files each import
/// would copy, each with a box, those imported before unchecked, and how much each backup would
/// copy. Run runs the actions with the files left checked; the dialog stays open, saying why, when
/// it can't.
/// </summary>
/// <param name="actions">The drive's actions, as saved, which tell the backups from the imports.</param>
public sealed partial class DriveRunNowViewModel(ConnectedDriveInfo drive, string key, string name, IReadOnlyList<DriveAction> actions) : ObservableObject
{
    private static readonly CompositeFormat TitleFormat = CompositeFormat.Parse(Strings.DriveActionsRunNowTitle);
    private static readonly CompositeFormat ImportedBeforeFormat = CompositeFormat.Parse(Strings.DriveActionsRunNowImportedBefore);
    private static readonly CompositeFormat GoesToFormat = CompositeFormat.Parse(Strings.DriveActionsRunNowGoesTo);
    private static readonly CompositeFormat BackUpFormat = CompositeFormat.Parse(Strings.DriveActionsRunNowBackUp);

    private string _status = string.Empty;
    private bool _isLoaded;

    public string Title { get; } = string.Format(CultureInfo.CurrentCulture, TitleFormat, name);

    /// <summary>The files the imports would copy.</summary>
    public ObservableCollection<RunNowFile> Files { get; } = [];

    /// <summary>What the backups would copy, one line each.</summary>
    public ObservableCollection<string> Backups { get; } = [];

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

    public bool CanRun => _isLoaded;

    public async Task LoadAsync()
    {
        Status = Strings.DriveActionsRunNowLoading;
        var answer = await DriveActionsHost.AskAsync(Request(DriveActionsRequestKind.RunNowList), DriveActionsHost.ListTimeout).ConfigureAwait(true);
        if (answer is not { Status: DriveActionsAnswerStatus.Done })
        {
            Status = answer is null ? Strings.DriveActionsNotAnswering : Strings.DriveActionsAddGone;
            return;
        }

        foreach (var action in answer.Actions)
        {
            foreach (var file in action.Files)
            {
                var goesTo = string.Format(CultureInfo.CurrentCulture, GoesToFormat, file.Destination);
                Files.Add(new RunNowFile(
                    action.Action,
                    file.Path,
                    file.ImportedBefore ? string.Format(CultureInfo.CurrentCulture, ImportedBeforeFormat, goesTo) : goesTo,
                    !file.ImportedBefore));
            }

            if (action.Action >= 0 && action.Action < actions.Count && actions[action.Action].KnownAction == DriveActionKind.BackUp)
            {
                Backups.Add(string.Format(CultureInfo.CurrentCulture, BackUpFormat, PluralStrings.Format("DriveActionsRunNowFiles", action.Copies), ByteSizes.Format(action.Bytes)));
            }
        }

        _isLoaded = true;
        OnPropertyChanged(nameof(CanRun));
        Status = Files.Count == 0 && Backups.Count == 0 ? Strings.DriveActionsRunNowNothing : string.Empty;
    }

    /// <summary>Asks the host to run the actions with the files left checked; returns whether it did, and otherwise says why.</summary>
    public async Task<bool> RunAsync()
    {
        var chosen = Files.GroupBy(file => file.Action)
            .Select(group => new ChosenImport { Action = group.Key, Files = [.. group.Where(file => file.IsChosen).Select(file => file.Path)] })
            .ToList();
        var request = Request(DriveActionsRequestKind.RunNow);
        request.Chosen = chosen;
        var answer = await DriveActionsHost.AskAsync(request, DriveActionsHost.QuickTimeout).ConfigureAwait(true);
        Status = answer?.Status switch
        {
            DriveActionsAnswerStatus.Done => string.Empty,
            null => Strings.DriveActionsNotAnswering,
            DriveActionsAnswerStatus.NotConnected => Strings.DriveActionsAddGone,
            _ => Strings.DriveActionsRunNowRefused,
        };
        return Status.Length == 0;
    }

    private DriveActionsRequest Request(DriveActionsRequestKind kind) =>
        new() { Kind = kind, Letter = drive.Letter, Volume = drive.Volume, Key = key };
}
