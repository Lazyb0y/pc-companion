using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Settings;
using PCCompanion.Infrastructure;
using PCCompanion.Localization;
using PCCompanion.Modules.Files;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>An action in a drive's list on the page.</summary>
/// <param name="DriveIndex">The drive's place in the list, from 0.</param>
/// <param name="Index">The action's place in the drive's list, from 0.</param>
/// <param name="Summary">What the action does, or why it isn't used.</param>
/// <param name="CanEdit">The action is one this version knows, so the editor can open it.</param>
public sealed record DriveActionItem(int DriveIndex, int Index, string Name, string Summary, string EditName, string MoreName, bool CanEdit, bool CanMoveUp, bool CanMoveDown)
{
    /// <summary>The drive's and the action's places, for the action's buttons.</summary>
    public string Tag => FormattableString.Invariant($"{DriveIndex}:{Index}");
}

/// <summary>
/// A drive in the list on the page. What's connected now changes while the page is open, so those
/// properties tell the page when they change; the others come from the settings.
/// </summary>
public sealed partial class DriveItem : ObservableObject
{
    private string _status = string.Empty;
    private string _lookAlike = string.Empty;
    private string _notRecognized = string.Empty;
    private bool _isRunning;
    private bool _canRunNow;

    public DriveItem(int index, DriveEntry drive, string problem, bool receivesBackups, IReadOnlyList<DriveActionItem> actions)
    {
        ArgumentNullException.ThrowIfNull(drive);

        Index = index;
        Name = drive.Name.Trim();
        Key = DriveRecordsStore.KeyOf(drive);
        Kind = drive.KnownKind ?? DriveEntryKind.Drive;
        Glyph = drive.KnownKind == DriveEntryKind.CameraCards ? "\uE722" : "\uE88E";
        Problem = problem;
        ReceivesBackups = receivesBackups;
        WhenConnected = drive.KnownWhenConnected switch
        {
            WhenConnectedChoice.Ask => 0,
            WhenConnectedChoice.Act => 1,
            WhenConnectedChoice.Nothing => 2,
            _ => -1,
        };
        EjectWhenDone = drive.EjectWhenDone;
        Actions = actions;
        MoreName = Format(DriveActionsViewModel.MoreFormat, Name);
        AddActionName = Format(DriveActionsViewModel.AddActionFormat, Name);
        RunNowName = Format(DriveActionsViewModel.RunNowFormat, Name);
        StopName = Format(DriveActionsViewModel.StopFormat, Name);
        CanAddAction = drive.KnownKind is not null && drive.Actions.Count < DriveActionsSettings.MaximumActions;
        WhenConnectedName = Format(DriveActionsViewModel.WhenConnectedFormat, Name);
        EjectName = Format(DriveActionsViewModel.EjectFormat, Name);
    }

    public int Index { get; }

    public string Name { get; }

    /// <summary>The key the host knows the drive by: its ID, or Camera cards' key.</summary>
    public string Key { get; }

    public DriveEntryKind Kind { get; }

    public string Glyph { get; }

    /// <summary>Why the drive isn't used, or an empty string.</summary>
    public string Problem { get; }

    public bool HasProblem => Problem.Length > 0;

    /// <summary>The drive has a backup, so its note recommends BitLocker.</summary>
    public bool ReceivesBackups { get; }

    /// <summary>When connected, as the place of the choice: Ask first, Act at once, Do nothing; -1 for one this version doesn't know.</summary>
    public int WhenConnected { get; }

    public bool EjectWhenDone { get; }

    /// <summary>When connected's choices, in the order of <see cref="WhenConnected"/>.</summary>
    public IReadOnlyList<string> WhenConnectedChoices { get; } = [Strings.DriveActionsWhenAsk, Strings.DriveActionsWhenAct, Strings.DriveActionsWhenNothing];

    public IReadOnlyList<DriveActionItem> Actions { get; }

    public bool HasNoActions => Actions.Count == 0;

    public bool CanAddAction { get; }

    public string AddActionName { get; }

    public string MoreName { get; }

    public string WhenConnectedName { get; }

    public string RunNowName { get; }

    public string StopName { get; }

    public string EjectName { get; }

    /// <summary>Whether it's connected, running or locked; empty while the host doesn't answer.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>A note about a drive with this drive's ID but other serial numbers, or an empty string.</summary>
    public string LookAlike
    {
        get => _lookAlike;
        private set
        {
            if (SetProperty(ref _lookAlike, value))
            {
                OnPropertyChanged(nameof(HasLookAlike));
            }
        }
    }

    public bool HasLookAlike => LookAlike.Length > 0;

    /// <summary>A note about a drive with this drive's serial numbers but without its ID file, or an empty string.</summary>
    public string NotRecognized
    {
        get => _notRecognized;
        private set
        {
            if (SetProperty(ref _notRecognized, value))
            {
                OnPropertyChanged(nameof(HasNotRecognized));
            }
        }
    }

    public bool HasNotRecognized => NotRecognized.Length > 0;

    /// <summary>The drive's actions are running, which Stop stops.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsNotRunning));
            }
        }
    }

    public bool IsNotRunning => !IsRunning;

    /// <summary>The drive is connected and recognized, and can be used, so Run now can run its actions.</summary>
    public bool CanRunNow
    {
        get => _canRunNow;
        private set => SetProperty(ref _canRunNow, value);
    }

    /// <summary>The drive connected and recognized as this one, for Run now; or <see langword="null"/>.</summary>
    internal ConnectedDriveInfo? Recognized { get; private set; }

    /// <summary>The look-alike or the drive without its ID file, for Use this drive and Add again.</summary>
    internal ConnectedDriveInfo? Other { get; private set; }

    /// <summary>The root of the drive while it's connected and recognized, such as <c>E:\</c>, for Browse in the action editor; or <see langword="null"/>.</summary>
    internal string? Root { get; private set; }

    /// <summary>Shows what the host said about the connected drives, or nothing when it didn't answer.</summary>
    internal void Show(DriveActionsAnswer? answer)
    {
        if (answer is null)
        {
            (Status, LookAlike, NotRecognized, Other, Root, Recognized) = (string.Empty, string.Empty, string.Empty, null, null, null);
            (IsRunning, CanRunNow) = (false, false);
            return;
        }

        var drives = answer.Drives.Where(drive => drive.Key == Key).ToArray();
        var recognized = drives.Where(drive => drive.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards).ToArray();
        var letters = string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", recognized.Select(drive => DriveActionsText.Letter(drive.Letter)));
        var locked = drives.FirstOrDefault(drive => drive.Outcome == RecognitionOutcome.Locked);
        Root = recognized.Length > 0 ? DriveActionsText.Letter(recognized[0].Letter) + "\\" : null;
        Recognized = recognized.FirstOrDefault();
        IsRunning = answer.Running is { } running && recognized.Any(drive => drive.Letter == running.Letter.ToString());
        CanRunNow = !HasProblem && Recognized is not null && !IsRunning;
        Status = IsRunning
            ? Strings.DriveActionsStatusRunning
            : recognized.Length > 0 ? Format(DriveActionsViewModel.ConnectedFormat, letters)
            : locked is not null ? Format(DriveActionsViewModel.LockedFormat, DriveActionsText.Letter(locked.Letter))
            : Strings.DriveActionsStatusNotConnected;

        var lookAlike = drives.FirstOrDefault(drive => drive.Outcome == RecognitionOutcome.LookAlike);
        var notRecognized = drives.FirstOrDefault(drive => drive.Outcome == RecognitionOutcome.NotRecognized);
        LookAlike = lookAlike is null ? string.Empty : Format(DriveActionsViewModel.LookAlikeFormat, DriveActionsText.Letter(lookAlike.Letter));
        NotRecognized = notRecognized is null ? string.Empty : Format(DriveActionsViewModel.NotRecognizedFormat, DriveActionsText.Letter(notRecognized.Letter));
        Other = lookAlike ?? notRecognized;
    }

    private static string Format(CompositeFormat format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);
}

/// <summary>
/// The Drive Actions page. Every change is saved right away. Which drives are connected comes from
/// the host, which the page asks while it's open, since only the host knows them.
/// </summary>
public sealed partial class DriveActionsViewModel : FeatureViewModel<DriveActionsSettings>
{
    internal static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.DriveActionsMoreName);
    internal static readonly CompositeFormat AddActionFormat = CompositeFormat.Parse(Strings.DriveActionsAddActionName);
    internal static readonly CompositeFormat RunNowFormat = CompositeFormat.Parse(Strings.DriveActionsRunNowName);
    internal static readonly CompositeFormat StopFormat = CompositeFormat.Parse(Strings.DriveActionsStopName);
    internal static readonly CompositeFormat WhenConnectedFormat = CompositeFormat.Parse(Strings.DriveActionsWhenConnectedName);
    internal static readonly CompositeFormat EjectFormat = CompositeFormat.Parse(Strings.DriveActionsEjectName);
    internal static readonly CompositeFormat ConnectedFormat = CompositeFormat.Parse(Strings.DriveActionsStatusConnected);
    internal static readonly CompositeFormat LockedFormat = CompositeFormat.Parse(Strings.DriveActionsStatusLocked);
    internal static readonly CompositeFormat LookAlikeFormat = CompositeFormat.Parse(Strings.DriveActionsLookAlikeInfo);
    internal static readonly CompositeFormat NotRecognizedFormat = CompositeFormat.Parse(Strings.DriveActionsNotRecognizedInfo);
    private static readonly CompositeFormat ActionMoreFormat = CompositeFormat.Parse(Strings.DriveActionsActionMoreName);
    private static readonly CompositeFormat ActionEditFormat = CompositeFormat.Parse(Strings.DriveActionsEditName);
    private static readonly CompositeFormat AddFailedFormat = CompositeFormat.Parse(Strings.DriveActionsAddFailed);
    private static readonly CompositeFormat NumberedNameFormat = CompositeFormat.Parse(Strings.DriveActionsNumberedName);
    private static readonly CompositeFormat DrivesTooManyFormat = CompositeFormat.Parse(Strings.DriveActionsDriveTooMany);
    private static readonly CompositeFormat NameTooLongFormat = CompositeFormat.Parse(Strings.DriveActionsNameTooLong);

    private readonly AppSettings _appSettings;
    private readonly ILogger<DriveActionsViewModel> _logger;
    private DriveActionsAnswer? _connected;
    private int _unanswered;
    private Task? _refreshing;
    private string _note = string.Empty;

    public DriveActionsViewModel(FeatureSettingsEditor editor, AppDataPaths paths, AppSettings appSettings, ILogger<DriveActionsViewModel> logger)
        : base(editor, DriveActionsFeature.Definition)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(appSettings);
        ArgumentNullException.ThrowIfNull(logger);

        Places = SpecialFolders.ForCurrentUser(paths.Root);
        _appSettings = appSettings;
        _logger = logger;
        RefreshDrives();
    }

    /// <summary>The folders actions can't use.</summary>
    public SpecialFolders Places { get; }

    public ObservableCollection<DriveItem> Drives { get; } = [];

    /// <summary>What the drives' actions did, which the host gives while Drive Actions is on.</summary>
    public DriveActionsHistoryViewModel History { get; } = new();

    public bool HasNoDrives => Drives.Count == 0;

    public bool CanAddDrive => Settings.Drives.Count < DriveActionsSettings.MaximumDrives;

    /// <summary>Whether the list has Camera cards, which it has once at most.</summary>
    public bool HasCameraCards => Settings.Drives.Any(drive => drive.KnownKind == DriveEntryKind.CameraCards);

    /// <summary>What went wrong with the last change that needed the host, such as adding a drive, or an empty string.</summary>
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

    /// <summary>
    /// Drive Actions is on, but the host didn't say which drives are connected the last two times,
    /// as while PC Companion isn't running; once is a host that's still starting the feature.
    /// </summary>
    public bool IsNotAnswering => IsOn && !IsPaused && _unanswered >= 2;

    /// <summary>Whether the host runs as administrator after a UAC prompt, while Drive Actions is paused.</summary>
    public bool IsPaused => HostLauncher.RunsUacElevated(_appSettings.General);

    /// <summary>
    /// Asks the host which drives are connected, and shows it; a call while the host is being asked
    /// waits for that answer. While Drive Actions is off or paused, nothing is connected as far as
    /// the page knows.
    /// </summary>
    public async Task RefreshConnectedAsync()
    {
        if (_refreshing is { } asking)
        {
            await asking.ConfigureAwait(true);
            return;
        }

        var refresh = AskConnectedAsync();
        _refreshing = refresh;
        try
        {
            await refresh.ConfigureAwait(true);
        }
        finally
        {
            if (_refreshing == refresh)
            {
                _refreshing = null;
            }
        }
    }

    /// <summary>The drives connected now, for Add a drive, or <see langword="null"/> when the host doesn't answer.</summary>
    public async Task<IReadOnlyList<ConnectedDriveInfo>?> ConnectedDrivesAsync()
    {
        await RefreshConnectedAsync().ConfigureAwait(true);
        return _connected?.Drives;
    }

    /// <summary>Adds Camera cards, with an import of a camera's files into the Pictures folder by month. Returns why it wasn't, or an empty string.</summary>
    public string AddCameraCards()
    {
        if (HasCameraCards)
        {
            return Strings.DriveActionsAddCameraCardsThere;
        }

        if (!CanAddDrive)
        {
            return TooMany();
        }

        var import = new DriveAction
        {
            Action = nameof(DriveActionKind.Import),
            From = DriveAction.DefaultFrom,
            Extensions = FileKinds.Extensions(FileKind.CameraFiles),
            Destination = System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "{year}", "{month}"),
            IfExists = nameof(NameConflict.AddNumber),
        };
        SaveDrives([.. Settings.Drives, new DriveEntry { Name = UniqueName(Strings.DriveActionsCameraCards), Kind = nameof(DriveEntryKind.CameraCards), Actions = [import] }]);
        return string.Empty;
    }

    /// <summary>
    /// Asks the host to add a connected drive, which writes its ID file, and adds it to the list.
    /// Returns why it wasn't added, or an empty string.
    /// </summary>
    public async Task<string> AddDriveAsync(ConnectedDriveInfo drive)
    {
        ArgumentNullException.ThrowIfNull(drive);

        if (!CanAddDrive)
        {
            return TooMany();
        }

        var answer = await AskAsync(DriveActionsRequestKind.AddDrive, drive, DriveActionsText.Of(drive.Kind)).ConfigureAwait(true);
        if (answer is { Status: DriveActionsAnswerStatus.Done, Entry: { } entry })
        {
            SaveDrives([.. Settings.Drives, entry with { Name = UniqueName(entry.Name) }]);
            _ = RefreshConnectedAsync();
            return string.Empty;
        }

        return Problem(answer);
    }

    /// <summary>
    /// Records the serial numbers of the look-alike connected for the drive, after the user
    /// confirmed that it's theirs. Returns why it didn't, or an empty string.
    /// </summary>
    public async Task<string> UseThisDriveAsync(int index)
    {
        if (index < 0 || index >= Drives.Count || Drives[index].Other is not { Outcome: RecognitionOutcome.LookAlike } other)
        {
            return string.Empty;
        }

        var answer = await AskAsync(DriveActionsRequestKind.UseDrive, other, string.Empty, Drives[index].Key).ConfigureAwait(true);
        return SerialsFrom(index, answer, withId: false);
    }

    /// <summary>
    /// Writes a new ID file on the drive connected with this drive's serial numbers but without its
    /// ID file, and records it. Returns why it didn't, or an empty string.
    /// </summary>
    public async Task<string> AddAgainAsync(int index)
    {
        if (index < 0 || index >= Drives.Count || Drives[index].Other is not { Outcome: RecognitionOutcome.NotRecognized } other)
        {
            return string.Empty;
        }

        var answer = await AskAsync(DriveActionsRequestKind.AddDrive, other, Drives[index].Name).ConfigureAwait(true);
        return SerialsFrom(index, answer, withId: true);
    }

    /// <summary>Why <paramref name="name"/> can't be a drive's new name, or an empty string.</summary>
    public string CheckName(int index, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Strings.DriveActionsNameEmpty;
        }

        if (trimmed.Length > DriveActionsSettings.MaximumNameLength)
        {
            return string.Format(CultureInfo.CurrentCulture, NameTooLongFormat, DriveActionsSettings.MaximumNameLength);
        }

        return Settings.Drives.Where((_, at) => at != index).Any(drive => string.Equals(drive.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ? Strings.DriveActionsNameTaken
            : string.Empty;
    }

    public void Rename(int index, string name) => ChangeDrive(index, drive => drive with { Name = name.Trim() });

    public void RemoveDrive(int index)
    {
        if (index >= 0 && index < Settings.Drives.Count)
        {
            SaveDrives([.. Settings.Drives.Where((_, at) => at != index)]);
        }
    }

    /// <summary>
    /// Saves When connected as the place of the choice: Ask first, Act at once, Do nothing. A choice
    /// and a switch save without building the list again, which would take the focus from them,
    /// unless the choice was one this version doesn't know, which was the drive's problem.
    /// </summary>
    public void SetWhenConnected(int index, int choice)
    {
        if (index < 0 || index >= Settings.Drives.Count || choice is < 0 or > 2)
        {
            return;
        }

        var known = Settings.Drives[index].KnownWhenConnected;
        var chosen = choice switch
        {
            0 => WhenConnectedChoice.Ask,
            1 => WhenConnectedChoice.Act,
            _ => WhenConnectedChoice.Nothing,
        };
        if (known != chosen)
        {
            ChangeDrive(index, drive => drive with { WhenConnected = chosen.ToString() }, rebuild: known is null);
        }
    }

    public void SetEjectWhenDone(int index, bool eject)
    {
        if (index >= 0 && index < Settings.Drives.Count && Settings.Drives[index].EjectWhenDone != eject)
        {
            ChangeDrive(index, drive => drive with { EjectWhenDone = eject }, rebuild: false);
        }
    }

    /// <summary>
    /// The action editor for a new action: an import with the defaults for Camera cards, which only
    /// bring files to the PC, and a backup for a drive.
    /// </summary>
    public DriveActionEditor NewEditor(int drive)
    {
        var kind = Drives[drive].Kind;
        var action = kind == DriveEntryKind.CameraCards
            ? new DriveAction
            {
                Action = nameof(DriveActionKind.Import),
                Extensions = FileKinds.Extensions(FileKind.CameraFiles),
                Destination = System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "{year}", "{month}"),
            }
            : new DriveAction { Action = nameof(DriveActionKind.BackUp), Destination = DriveAction.DefaultBackupDestination };
        return new DriveActionEditor(action, isNew: true, kind, Places, Drives[drive].Root);
    }

    /// <summary>The action editor for an action in the list, as saved.</summary>
    public DriveActionEditor EditorFor(int drive, int index) =>
        new(Settings.Drives[drive].Actions[index], isNew: false, Drives[drive].Kind, Places, Drives[drive].Root);

    /// <summary>Run now for the drive, while it's connected and recognized; or <see langword="null"/>.</summary>
    public DriveRunNowViewModel? RunNowFor(int index) =>
        index >= 0 && index < Drives.Count && Drives[index].Recognized is { } drive
            ? new DriveRunNowViewModel(drive, Drives[index].Key, Drives[index].Name, Settings.Drives[index].Actions)
            : null;

    /// <summary>Stops the run that's going after the file it's copying.</summary>
    public async Task StopAsync()
    {
        Note = await DriveActionsHost.AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.Stop }, DriveActionsHost.QuickTimeout).ConfigureAwait(true) is null
            ? Strings.DriveActionsNotAnswering
            : string.Empty;
        await RefreshConnectedAsync().ConfigureAwait(true);
    }

    public void AddAction(int drive, DriveAction action) => ChangeActions(drive, actions => [.. actions, action]);

    public void ReplaceAction(int drive, int index, DriveAction action) =>
        ChangeActions(drive, actions => [.. actions.Select((existing, at) => at == index ? action : existing)]);

    public void RemoveAction(int drive, int index) => ChangeActions(drive, actions => [.. actions.Where((_, at) => at != index)]);

    /// <summary>Moves an action one place up, with an offset of -1, or down, with 1.</summary>
    public void MoveAction(int drive, int index, int offset) => ChangeActions(drive, actions =>
    {
        var target = index + offset;
        if (index < 0 || target < 0 || index >= actions.Count || target >= actions.Count)
        {
            return actions;
        }

        var moved = actions.ToList();
        (moved[index], moved[target]) = (moved[target], moved[index]);
        return moved;
    });

    private async Task AskConnectedAsync()
    {
        _connected = IsOn && !IsPaused
            ? await DriveActionsHost.AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.Drives }, DriveActionsHost.QuickTimeout).ConfigureAwait(true)
            : null;
        _unanswered = _connected is null && IsOn && !IsPaused ? _unanswered + 1 : 0;
        foreach (var drive in Drives)
        {
            drive.Show(_connected);
        }

        OnPropertyChanged(nameof(IsNotAnswering));
    }

    [RelayCommand]
    private void OpenAutoPlaySettings()
    {
        if (!ShellActions.OpenAutoPlaySettings())
        {
            LogCantOpenSettings(_logger);
        }
    }

    private static Task<DriveActionsAnswer?> AskAsync(DriveActionsRequestKind kind, ConnectedDriveInfo drive, string name, string key = "") =>
        DriveActionsHost.AskAsync(new DriveActionsRequest { Kind = kind, Letter = drive.Letter, Volume = drive.Volume, Name = name, Key = key }, DriveActionsHost.QuickTimeout);

    // Takes the serial numbers, and for Add again the ID, from the host's entry; the rest of the
    // drive stays as the page has it.
    private string SerialsFrom(int index, DriveActionsAnswer? answer, bool withId)
    {
        if (answer is not { Status: DriveActionsAnswerStatus.Done, Entry: { } entry })
        {
            return Note = Problem(answer);
        }

        ChangeDrive(index, drive => drive with
        {
            Id = withId ? entry.Id : drive.Id,
            VolumeSerial = entry.VolumeSerial,
            HardwareSerial = entry.HardwareSerial,
        });
        _ = RefreshConnectedAsync();
        return string.Empty;
    }

    private static string Problem(DriveActionsAnswer? answer) => answer?.Status switch
    {
        null => Strings.DriveActionsNotAnswering,
        DriveActionsAnswerStatus.CantAdd => DriveActionsText.Of(answer.AddProblem),
        DriveActionsAnswerStatus.Failed => string.Format(CultureInfo.CurrentCulture, AddFailedFormat, answer.ErrorCode),
        _ => Strings.DriveActionsAddGone,
    };

    private static string TooMany() => string.Format(CultureInfo.CurrentCulture, DrivesTooManyFormat, DriveActionsSettings.MaximumDrives);

    // A name no drive in the list has, ignoring case: the name, or the name with a number, which
    // a long name makes room for.
    private string UniqueName(string name)
    {
        var trimmed = name.Trim();
        var candidate = trimmed[..Math.Min(trimmed.Length, DriveActionsSettings.MaximumNameLength)];
        for (var number = 2; Settings.Drives.Any(drive => string.Equals(drive.Name.Trim(), candidate, StringComparison.OrdinalIgnoreCase)); number++)
        {
            candidate = string.Format(CultureInfo.CurrentCulture, NumberedNameFormat, trimmed[..Math.Min(trimmed.Length, DriveActionsSettings.MaximumNameLength - 6)], number);
        }

        return candidate;
    }

    private void ChangeDrive(int index, Func<DriveEntry, DriveEntry> change, bool rebuild = true)
    {
        if (index >= 0 && index < Settings.Drives.Count)
        {
            SaveDrives([.. Settings.Drives.Select((drive, at) => at == index ? change(drive) : drive)], rebuild);
        }
    }

    private void ChangeActions(int drive, Func<IReadOnlyList<DriveAction>, IReadOnlyList<DriveAction>> change) =>
        ChangeDrive(drive, entry => entry with { Actions = change(entry.Actions) });

    // Any change ends the note about the last one.
    private void SaveDrives(IReadOnlyList<DriveEntry> drives, bool rebuild = true)
    {
        Note = string.Empty;
        Save(Settings with { Drives = drives });
        if (rebuild)
        {
            RefreshDrives();
        }
    }

    private void RefreshDrives()
    {
        var reviews = DriveActionsCheck.Review(Settings, Places);
        Drives.Clear();
        for (var index = 0; index < Settings.Drives.Count; index++)
        {
            var item = ToItem(index, Settings.Drives[index], reviews[index]);
            item.Show(_connected);
            Drives.Add(item);
        }

        OnPropertyChanged(nameof(HasNoDrives));
        OnPropertyChanged(nameof(CanAddDrive));
        OnPropertyChanged(nameof(HasCameraCards));
    }

    private DriveItem ToItem(int index, DriveEntry drive, DriveReview review)
    {
        var actions = drive.Actions.Select((action, at) => new DriveActionItem(
            index,
            at,
            DriveActionsText.NameOf(action),
            DriveActionsText.Summary(action, DriveActionsText.Of(review.Actions[at], action, Places)),
            string.Format(CultureInfo.CurrentCulture, ActionEditFormat, at + 1, drive.Name.Trim()),
            string.Format(CultureInfo.CurrentCulture, ActionMoreFormat, at + 1, drive.Name.Trim()),
            action.KnownAction is not null && drive.KnownKind is not null,
            at > 0,
            at < drive.Actions.Count - 1)).ToList();

        return new DriveItem(
            index,
            drive,
            DriveActionsText.Of(review.Problem),
            drive.Actions.Any(action => action.KnownAction == DriveActionKind.BackUp),
            actions);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open its AutoPlay settings for Drive Actions.")]
    private static partial void LogCantOpenSettings(ILogger logger);
}
