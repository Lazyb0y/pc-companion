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

/// <summary>A file of a run in the history on the page.</summary>
/// <param name="Run">The run's place in the history, newest first, from 0.</param>
/// <param name="What">What happened to the file, and whether Undo put it back.</param>
public sealed record RunFileItem(int Run, string Path, string What, bool CanUndo, string UndoName)
{
    /// <summary>The run's place and the file's path, for its Undo button.</summary>
    public string Tag => FormattableString.Invariant($"{Run}:{Path}");
}

/// <summary>A run in the history on the page.</summary>
/// <param name="Index">Its place in the history, newest first, from 0.</param>
/// <param name="When">When it started.</param>
/// <param name="What">What its actions did, and how it ended.</param>
/// <param name="CanUndo">It imported files that Undo can put back.</param>
public sealed record RunItem(int Index, string Name, string When, string What, bool CanUndo, string UndoName, IReadOnlyList<RunFileItem> Files)
{
    /// <summary>When it started, and below that what it did.</summary>
    public string Description => When + Environment.NewLine + What;

    public bool HasFiles => Files.Count > 0;
}

/// <summary>
/// The history on the Drive Actions page, which the host gives through its pipe while Drive Actions
/// is on, with Undo and Clear history. It shows the newest runs, more on request.
/// </summary>
public sealed partial class DriveActionsHistoryViewModel : ObservableObject
{
    // How many more runs Show more adds; the history keeps up to 1,000.
    private const int Page = 50;

    private static readonly CompositeFormat UndoRunFormat = CompositeFormat.Parse(Strings.DriveActionsUndoRunName);
    private static readonly CompositeFormat UndoFileFormat = CompositeFormat.Parse(Strings.DriveActionsUndoFileName);
    private static readonly CompositeFormat UndoneFormat = CompositeFormat.Parse(Strings.DriveActionsUndoneNote);

    private IReadOnlyList<RunEntry> _runs = [];
    private int _shown = Page;
    private string _status = string.Empty;
    private string _note = string.Empty;
    private bool _isLoaded;

    public ObservableCollection<RunItem> Items { get; } = [];

    /// <summary>Why the list is empty, or an empty string.</summary>
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

    /// <summary>What the last Undo or Run now said, or an empty string.</summary>
    public string Note
    {
        get => _note;
        private set
        {
            if (SetProperty(ref _note, value))
            {
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    public bool HasNote => Note.Length > 0;

    public bool HasMore => _runs.Count > Items.Count;

    /// <summary>Whether the host gave the history, so it can be cleared.</summary>
    public bool CanClear => _isLoaded && _runs.Count > 0;

    /// <summary>Asks the host for the history again.</summary>
    public async Task LoadAsync()
    {
        if (Items.Count == 0)
        {
            Status = Strings.DriveActionsHistoryLoading;
        }

        var answer = await DriveActionsHost.AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.History }, DriveActionsHost.QuickTimeout).ConfigureAwait(true);
        _isLoaded = answer is { Status: DriveActionsAnswerStatus.Done };
        _runs = _isLoaded ? answer!.Runs : [];
        Show();
        Status = !_isLoaded ? Strings.DriveActionsHistoryNotRunning : _runs.Count == 0 ? Strings.DriveActionsHistoryEmpty : string.Empty;
    }

    public void ShowMore()
    {
        _shown += Page;
        Show();
    }

    /// <summary>The run at <paramref name="index"/>'s drive and start, for Undo's question.</summary>
    public RunItem? RunAt(int index) => index >= 0 && index < Items.Count ? Items[index] : null;

    /// <summary>
    /// Asks the host to send the run's imported copies, or one of them, to the Recycle Bin, and says
    /// what it did.
    /// </summary>
    public async Task UndoAsync(int index, string file = "")
    {
        if (index < 0 || index >= _runs.Count)
        {
            return;
        }

        var run = _runs[index];
        var answer = await DriveActionsHost.AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.Undo, Run = run.Id, Started = run.Started, File = file }, DriveActionsHost.UndoTimeout).ConfigureAwait(true);
        Note = answer switch
        {
            null => Strings.DriveActionsHistoryNotRunning,
            { Status: DriveActionsAnswerStatus.Done, NotUndone: 0 } => string.Empty,
            { Status: DriveActionsAnswerStatus.Done } => string.Format(CultureInfo.CurrentCulture, UndoneFormat, answer.Undone, answer.NotUndone),
            _ => Strings.DriveActionsUndoRefused,
        };
        await LoadAsync().ConfigureAwait(true);
    }

    public async Task ClearAsync()
    {
        var answer = await DriveActionsHost.AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.ClearHistory }, DriveActionsHost.QuickTimeout).ConfigureAwait(true);
        Note = answer is { Status: DriveActionsAnswerStatus.Done } ? string.Empty : Strings.DriveActionsHistoryNotRunning;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Says that Run now started the run, and shows the history as it is.</summary>
    public async Task RanAsync()
    {
        Note = Strings.DriveActionsRunNowStarted;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the Recycle Bin, where an undone copy is restored.</summary>
    public static void OpenRecycleBin() => ShellActions.OpenRecycleBin();

    private void Show()
    {
        Items.Clear();
        foreach (var (run, index) in _runs.Take(_shown).Select((run, index) => (run, index)))
        {
            var when = run.Started.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var files = run.Actions
                .SelectMany(action => action.Files)
                .Select(file => new RunFileItem(
                    index,
                    file.Path,
                    DriveActionsText.Of(file),
                    file is { Outcome: FileOutcome.Imported, Undone: false, NewPath: not null },
                    string.Format(CultureInfo.CurrentCulture, UndoFileFormat, file.Path)))
                .ToList();
            Items.Add(new RunItem(
                index,
                run.Drive,
                when,
                DriveActionsText.Of(run),
                files.Any(file => file.CanUndo),
                string.Format(CultureInfo.CurrentCulture, UndoRunFormat, run.Drive, when),
                files));
        }

        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanClear));
    }
}
