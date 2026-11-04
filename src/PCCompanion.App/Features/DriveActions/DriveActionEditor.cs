using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files;
using PCCompanion.Modules.Files.DriveActions;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>A folder in a backup's list in the action editor.</summary>
/// <param name="Index">Its place in the list, from 0.</param>
/// <param name="RemoveName">The accessible name of its Remove button.</param>
public sealed record BackupFolderItem(int Index, string Path, string RemoveName);

/// <summary>
/// The action editor: what an action does, with the boxes of its kind, and why it can't be used
/// below each box. Save is available only for an action that can be used, as the host checks it.
/// </summary>
public sealed partial class DriveActionEditor : ObservableObject
{
    private static readonly CompositeFormat RemoveFolderFormat = CompositeFormat.Parse(Strings.DriveActionsRemoveFolderName);

    // Every property that depends on the fields, which change together.
    private static readonly string[] Dependents =
    [
        nameof(IsImport), nameof(IsBackUp), nameof(IsOpenFolder),
        nameof(FromProblem), nameof(HasFromProblem), nameof(ExtensionsProblem), nameof(HasExtensionsProblem),
        nameof(DestinationProblem), nameof(HasDestinationProblem), nameof(FoldersProblem), nameof(HasFoldersProblem),
        nameof(BackupDestinationProblem), nameof(HasBackupDestinationProblem), nameof(OpenFolderProblem), nameof(HasOpenFolderProblem),
        nameof(CanAddFolder), nameof(CanSave),
    ];

    private readonly DriveEntryKind _kind;
    private readonly SpecialFolders _places;
    private readonly IReadOnlyList<DriveActionKind> _kinds;
    private int _actionIndex;
    private string _from;
    private string _fromChoice = string.Empty;
    private string _extensions;
    private string _destination;
    private int _ifExistsIndex;
    private string _backupDestination;
    private string _folder;

    /// <param name="root">The root of the drive while it's connected, such as <c>E:\</c>, for Browse on the drive; or <see langword="null"/>.</param>
    public DriveActionEditor(DriveAction action, bool isNew, DriveEntryKind kind, SpecialFolders places, string? root)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(places);

        _kind = kind;
        _places = places;
        Root = root;
        Title = isNew ? Strings.DriveActionsAddActionTitle : Strings.DriveActionsEditActionTitle;

        // Camera cards only bring files to the PC.
        _kinds = kind == DriveEntryKind.CameraCards
            ? [DriveActionKind.Import, DriveActionKind.OpenFolder]
            : [DriveActionKind.Import, DriveActionKind.BackUp, DriveActionKind.OpenFolder];
        ActionChoices = [.. _kinds.Select(choice => DriveActionsText.NameOf(new DriveAction { Action = choice.ToString() }))];
        _actionIndex = Math.Max(0, _kinds.ToList().IndexOf(action.KnownAction ?? DriveActionKind.Import));

        var isImport = action.KnownAction == DriveActionKind.Import;
        _from = isImport ? action.From : DriveAction.DefaultFrom;
        _extensions = string.Join("; ", isImport ? action.Extensions : FileKinds.Extensions(FileKind.CameraFiles));
        _destination = isImport ? action.Destination : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "{year}", "{month}");
        _ifExistsIndex = (isImport ? action.KnownIfExists : NameConflict.AddNumber) == NameConflict.Skip ? 1 : 0;
        var isBackUp = action.KnownAction == DriveActionKind.BackUp;
        _backupDestination = isBackUp ? action.Destination : DriveAction.DefaultBackupDestination;
        Folders = [.. (isBackUp ? action.Folders : []).Select((folder, index) => Item(index, folder))];
        _folder = action.KnownAction == DriveActionKind.OpenFolder ? action.Folder : string.Empty;
    }

    public string Title { get; }

    /// <summary>The root of the drive while it's connected, for Browse on the drive; or <see langword="null"/>.</summary>
    public string? Root { get; }

    public bool CanBrowseDrive => Root is not null;

    /// <summary>The actions the drive can have, in the order of <see cref="ActionIndex"/>.</summary>
    public IReadOnlyList<string> ActionChoices { get; }

    public int ActionIndex
    {
        get => _actionIndex;
        set => Change(ref _actionIndex, Math.Clamp(value, 0, _kinds.Count - 1));
    }

    public bool IsImport => Kind == DriveActionKind.Import;

    public bool IsBackUp => Kind == DriveActionKind.BackUp;

    public bool IsOpenFolder => Kind == DriveActionKind.OpenFolder;

    /// <summary>An import's folder on the drive, such as <c>DCIM</c>; empty for the whole drive.</summary>
    public string From
    {
        get => _from;
        set
        {
            _fromChoice = string.Empty;
            Change(ref _from, value);
        }
    }

    public string FromProblem => !IsImport ? string.Empty
        : _fromChoice.Length > 0 ? _fromChoice
        : !DriveActionsCheck.IsDriveFolder(TrimFolder(_from), allowRoot: true) ? Strings.DriveActionsActionInvalidDriveFolder
        : string.Empty;

    public bool HasFromProblem => FromProblem.Length > 0;

    /// <summary>An import's extensions, separated by semicolons.</summary>
    public string Extensions
    {
        get => _extensions;
        set => Change(ref _extensions, value);
    }

    public string ExtensionsProblem => !IsImport ? string.Empty
        : Split(_extensions) is not { Length: > 0 } extensions ? Strings.DriveActionsActionNoTypes
        : !extensions.All(DriveActionsCheck.IsExtension) ? Strings.DriveActionsActionInvalidType
        : string.Empty;

    public bool HasExtensionsProblem => ExtensionsProblem.Length > 0;

    /// <summary>Where an import copies to, on the PC, with parts in braces.</summary>
    public string Destination
    {
        get => _destination;
        set => Change(ref _destination, value);
    }

    public string DestinationProblem => IsImport ? DriveActionsText.Of(FolderChecks.CheckDestination(_destination.Trim(), _places)) : string.Empty;

    public bool HasDestinationProblem => DestinationProblem.Length > 0;

    /// <summary>When a file with that name is there: Add a number, or Skip.</summary>
    public int IfExistsIndex
    {
        get => _ifExistsIndex;
        set => Change(ref _ifExistsIndex, Math.Clamp(value, 0, 1));
    }

    /// <summary>A backup's folders on the PC.</summary>
    public ObservableCollection<BackupFolderItem> Folders { get; }

    public bool CanAddFolder => Folders.Count < DriveActionsSettings.MaximumFolders;

    public string FoldersProblem => IsBackUp && DriveActionsCheck.CheckAction(ToAction(), _kind, _places) is (ActionProblem.NoFolders or ActionProblem.TooManyFolders or ActionProblem.FolderNotAllowed or ActionProblem.SameFolderName) and var problem
        ? DriveActionsText.Of(problem, ToAction(), _places)
        : string.Empty;

    public bool HasFoldersProblem => FoldersProblem.Length > 0;

    /// <summary>A backup's folder on the drive, such as <c>Backup</c>.</summary>
    public string BackupDestination
    {
        get => _backupDestination;
        set => Change(ref _backupDestination, value);
    }

    public string BackupDestinationProblem => IsBackUp && !DriveActionsCheck.IsDriveFolder(TrimFolder(_backupDestination), allowRoot: false) ? Strings.DriveActionsActionInvalidDriveFolder : string.Empty;

    public bool HasBackupDestinationProblem => BackupDestinationProblem.Length > 0;

    /// <summary>The folder to open: on the drive, without its letter, or on the PC, with its full path.</summary>
    public string Folder
    {
        get => _folder;
        set => Change(ref _folder, value);
    }

    public string OpenFolderProblem => IsOpenFolder && DriveActionsCheck.CheckAction(ToAction(), _kind, _places) == ActionProblem.InvalidFolder ? Strings.DriveActionsActionInvalidFolder : string.Empty;

    public bool HasOpenFolderProblem => OpenFolderProblem.Length > 0;

    public bool CanSave => DriveActionsCheck.CheckAction(ToAction(), _kind, _places) == ActionProblem.None;

    private DriveActionKind Kind => _kinds[_actionIndex];

    /// <summary>Adds a kind's extensions that aren't in the list yet.</summary>
    public void AddKind(FileKind kind)
    {
        var extensions = Split(_extensions).ToList();
        extensions.AddRange(FileKinds.Extensions(kind).Where(extension => !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)));
        Extensions = string.Join("; ", extensions);
    }

    /// <summary>
    /// The folder on the drive for a path Browse chose, without the drive's letter, or
    /// <see langword="null"/> when the path isn't on the drive.
    /// </summary>
    public string? OnDrive(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Root is { } root && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.TrimEndingDirectorySeparator(path[root.Length..]) : null;
    }

    /// <summary>Takes the folder Browse chose as the import's folder, or says it isn't on the drive.</summary>
    public void ChooseFrom(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (OnDrive(path) is { } folder)
        {
            From = folder;
            return;
        }

        _fromChoice = Strings.DriveActionsNotOnDrive;
        Changed();
    }

    /// <summary>Adds a folder to back up, unless it's in the list already.</summary>
    public void AddFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var full = FolderPaths.Normalize(path, out var normalized) == FolderProblem.None ? normalized : path.Trim();
        if (CanAddFolder && !Folders.Any(folder => string.Equals(folder.Path, full, StringComparison.OrdinalIgnoreCase)))
        {
            Folders.Add(Item(Folders.Count, full));
            Changed();
        }
    }

    public void RemoveFolder(int index)
    {
        if (index < 0 || index >= Folders.Count)
        {
            return;
        }

        var paths = Folders.Where((_, at) => at != index).Select(folder => folder.Path).ToArray();
        Folders.Clear();
        foreach (var path in paths)
        {
            Folders.Add(Item(Folders.Count, path));
        }

        Changed();
    }

    /// <summary>The action as edited, with only its own kind's settings.</summary>
    public DriveAction ToAction() => Kind switch
    {
        DriveActionKind.Import => new DriveAction
        {
            Action = nameof(DriveActionKind.Import),
            From = TrimFolder(_from),
            Extensions = [.. Split(_extensions).Select(extension => extension.TrimStart('.').ToLowerInvariant())],
            Destination = _destination.Trim(),
            IfExists = _ifExistsIndex == 1 ? nameof(NameConflict.Skip) : nameof(NameConflict.AddNumber),
        },
        DriveActionKind.BackUp => new DriveAction
        {
            Action = nameof(DriveActionKind.BackUp),
            Folders = [.. Folders.Select(folder => folder.Path)],
            Destination = TrimFolder(_backupDestination),
        },
        _ => new DriveAction { Action = nameof(DriveActionKind.OpenFolder), Folder = TrimFolder(_folder) },
    };

    private static string[] Split(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // A folder on the drive as typed, with spaces around it and separators at its ends left out; a
    // full path on the PC keeps its root.
    private static string TrimFolder(string folder)
    {
        var trimmed = folder.Trim();
        return Path.IsPathFullyQualified(trimmed) ? trimmed : trimmed.Trim('\\', '/');
    }

    private static BackupFolderItem Item(int index, string path) =>
        new(index, path, string.Format(CultureInfo.CurrentCulture, RemoveFolderFormat, path));

    private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            Changed();
        }
    }

    private void Changed()
    {
        foreach (var name in Dependents)
        {
            OnPropertyChanged(name);
        }
    }
}
