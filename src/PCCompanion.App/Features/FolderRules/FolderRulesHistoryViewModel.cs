using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>An entry of the history on the page.</summary>
/// <param name="Index">The entry's place in the history, newest first, from 0.</param>
/// <param name="What">What happened to the file, and whether Undo put it back.</param>
/// <param name="When">When, and by which rule.</param>
public sealed record HistoryItem(int Index, string Name, string What, string When, bool CanUndo, string UndoName);

/// <summary>
/// The history on the Folder Rules page, which the host gives through its pipe while Folder
/// Rules is on, with Undo and Clear history. It shows the newest entries, more on request.
/// </summary>
public sealed partial class FolderRulesHistoryViewModel : ObservableObject
{
    // How many more entries Show more adds; the history keeps up to 1,000.
    private const int Page = 50;

    private static readonly CompositeFormat WhenFormat = CompositeFormat.Parse(Strings.FolderRulesHistoryWhen);
    private static readonly CompositeFormat UndoFormat = CompositeFormat.Parse(Strings.FolderRulesUndoName);

    private IReadOnlyList<HistoryEntry> _entries = [];
    private int _shown = Page;
    private string _status = string.Empty;
    private string _note = string.Empty;
    private bool _isLoaded;

    public ObservableCollection<HistoryItem> Items { get; } = [];

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

    public bool HasMore => _entries.Count > Items.Count;

    /// <summary>Whether the host gave the history, so it can be cleared.</summary>
    public bool CanClear => _isLoaded && _entries.Count > 0;

    /// <summary>Asks the host for the history again.</summary>
    public async Task LoadAsync()
    {
        if (Items.Count == 0)
        {
            Status = Strings.FolderRulesHistoryLoading;
        }

        var answer = await FolderRulesHost.AskAsync(new FolderRulesRequest { Kind = FolderRulesRequestKind.History }, FolderRulesHost.QuickTimeout).ConfigureAwait(true);
        _isLoaded = answer is { Status: FolderRulesAnswerStatus.Done };
        _entries = _isLoaded ? answer!.Entries : [];
        Show();
        Status = !_isLoaded ? Strings.FolderRulesHistoryNotRunning : _entries.Count == 0 ? Strings.FolderRulesHistoryEmpty : string.Empty;
    }

    public void ShowMore()
    {
        _shown += Page;
        Show();
    }

    /// <summary>Asks the host to put back the entry's file, and says why it didn't when it didn't.</summary>
    public async Task UndoAsync(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        var entry = _entries[index];
        var answer = await FolderRulesHost.AskAsync(new FolderRulesRequest { Kind = FolderRulesRequestKind.Undo, Entry = entry.Id, Time = entry.Time }, FolderRulesHost.UndoTimeout).ConfigureAwait(true);
        Note = answer is null ? Strings.FolderRulesHistoryNotRunning : FolderRulesText.OfUndo(answer);
        await LoadAsync().ConfigureAwait(true);
    }

    public async Task ClearAsync()
    {
        var answer = await FolderRulesHost.AskAsync(new FolderRulesRequest { Kind = FolderRulesRequestKind.ClearHistory }, FolderRulesHost.QuickTimeout).ConfigureAwait(true);
        Note = answer is { Status: FolderRulesAnswerStatus.Done } ? string.Empty : Strings.FolderRulesHistoryNotRunning;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Says that Run now handed the files to the host, and shows the history as it is.</summary>
    public async Task RanAsync()
    {
        Note = Strings.FolderRulesRunNowStarted;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the Recycle Bin, where a file a rule moved there is restored.</summary>
    public static void OpenRecycleBin() => ShellActions.OpenRecycleBin();

    private void Show()
    {
        Items.Clear();
        foreach (var (entry, index) in _entries.Take(_shown).Select((entry, index) => (entry, index)))
        {
            Items.Add(new HistoryItem(
                index,
                entry.Name,
                FolderRulesText.Of(entry),
                string.Format(CultureInfo.CurrentCulture, WhenFormat, entry.Time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), entry.Rule),
                entry.Undone is null && entry.Outcome is HistoryOutcome.Moved or HistoryOutcome.Copied or HistoryOutcome.Renamed,
                string.Format(CultureInfo.CurrentCulture, UndoFormat, entry.Name)));
        }

        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanClear));
    }
}
