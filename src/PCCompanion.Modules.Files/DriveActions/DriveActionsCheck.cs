using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>What keeps a drive in the list from being used.</summary>
public enum DriveProblem
{
    None,

    /// <summary>It has no name.</summary>
    NoName,

    /// <summary>Its name is longer than <see cref="DriveActionsSettings.MaximumNameLength"/> characters.</summary>
    NameTooLong,

    /// <summary>A drive earlier in the list has the same name, ignoring case.</summary>
    SameName,

    /// <summary>Its kind is one this version doesn't know, such as one from a newer version.</summary>
    UnknownKind,

    /// <summary>What happens when it's connected is a choice this version doesn't know.</summary>
    UnknownWhenConnected,

    /// <summary>A drive without a well-formed ID and volume serial, which could never be recognized.</summary>
    NotAdded,

    /// <summary>Camera cards is in the list already, earlier.</summary>
    SecondCameraCards,

    /// <summary>It comes after the first <see cref="DriveActionsSettings.MaximumDrives"/> drives.</summary>
    TooMany,
}

/// <summary>What keeps an action from being used.</summary>
public enum ActionProblem
{
    None,

    /// <summary>The action is one this version doesn't know, such as one from a newer version.</summary>
    UnknownAction,

    /// <summary>The folder on the drive isn't a folder without the drive's letter, such as <c>DCIM</c>.</summary>
    InvalidDriveFolder,

    /// <summary>An import without any extension, which would import every file.</summary>
    NoTypes,

    /// <summary>An extension holds a dot, a separator or a character a file name can't have.</summary>
    InvalidType,

    /// <summary>
    /// An import's destination can't be used; <see cref="FolderChecks.CheckDestination"/> says why.
    /// </summary>
    DestinationNotAllowed,

    /// <summary>What an import does when the name is taken is a choice this version doesn't know.</summary>
    UnknownIfExists,

    /// <summary>A backup for Camera cards, which only ever bring files to the PC.</summary>
    NotForCameraCards,

    /// <summary>A backup without folders.</summary>
    NoFolders,

    /// <summary>A backup with more than <see cref="DriveActionsSettings.MaximumFolders"/> folders.</summary>
    TooManyFolders,

    /// <summary>
    /// A backup's folder can't be used, such as a drive's root; <see cref="FolderChecks.CheckFolder"/>
    /// says why for the others.
    /// </summary>
    FolderNotAllowed,

    /// <summary>Two of a backup's folders have the same name, which would share a folder on the drive.</summary>
    SameFolderName,

    /// <summary>The folder to open is neither a full path on the PC nor a folder on the drive.</summary>
    InvalidFolder,

    /// <summary>It comes after the drive's first <see cref="DriveActionsSettings.MaximumActions"/> actions.</summary>
    TooMany,
}

/// <summary>A drive's problem and its actions' problems, in the order of the list.</summary>
public sealed record DriveReview(DriveProblem Problem, IReadOnlyList<ActionProblem> Actions);

/// <summary>
/// Checks the drives and their actions, for the host and for the page. See "Drives" and "Actions"
/// in docs/features/files/drive-actions.md.
/// </summary>
public static class DriveActionsCheck
{
    // The longest a folder on a drive, without its letter, can be: Windows' usual limit for a path
    // less the letter, the separator and room for the files in it.
    private const int MaximumDriveFolderLength = 200;

    private static readonly char[] InvalidExtensionCharacters = [.. Path.GetInvalidFileNameChars(), '.', '*', '?'];

    /// <summary>
    /// The problem of each drive and action in <paramref name="settings"/>, including those beyond
    /// the limits.
    /// </summary>
    public static IReadOnlyList<DriveReview> Review(DriveActionsSettings settings, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(places);

        var reviews = new List<DriveReview>();
        var names = new List<string>();
        var cameraCards = false;
        foreach (var drive in settings.Drives)
        {
            if (reviews.Count >= DriveActionsSettings.MaximumDrives)
            {
                reviews.Add(new DriveReview(DriveProblem.TooMany, [.. drive.Actions.Select(_ => ActionProblem.TooMany)]));
                continue;
            }

            var problem = CheckDrive(drive);
            if (problem == DriveProblem.None && names.Contains(drive.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                problem = DriveProblem.SameName;
            }

            if (problem == DriveProblem.None && drive.KnownKind == DriveEntryKind.CameraCards)
            {
                problem = cameraCards ? DriveProblem.SecondCameraCards : DriveProblem.None;
                cameraCards = true;
            }

            names.Add(drive.Name.Trim());
            var actions = drive.Actions.Select((action, index) => index >= DriveActionsSettings.MaximumActions
                ? ActionProblem.TooMany
                : CheckAction(action, drive.KnownKind ?? DriveEntryKind.Drive, places));
            reviews.Add(new DriveReview(problem, [.. actions]));
        }

        return reviews;
    }

    /// <summary>The first problem of a drive itself, without comparing it with the others in the list.</summary>
    public static DriveProblem CheckDrive(DriveEntry drive)
    {
        ArgumentNullException.ThrowIfNull(drive);

        if (drive.Name.Trim().Length == 0)
        {
            return DriveProblem.NoName;
        }

        if (drive.Name.Trim().Length > DriveActionsSettings.MaximumNameLength)
        {
            return DriveProblem.NameTooLong;
        }

        if (drive.KnownKind is not { } kind)
        {
            return DriveProblem.UnknownKind;
        }

        if (drive.KnownWhenConnected is null)
        {
            return DriveProblem.UnknownWhenConnected;
        }

        return kind == DriveEntryKind.Drive
            && !(DriveIdentity.IsId(drive.Id) && DriveIdentity.IsHash(drive.VolumeSerial) && (drive.HardwareSerial.Length == 0 || DriveIdentity.IsHash(drive.HardwareSerial)))
            ? DriveProblem.NotAdded
            : DriveProblem.None;
    }

    /// <summary>The first problem of an action of a drive of <paramref name="kind"/>, or <see cref="ActionProblem.None"/>.</summary>
    public static ActionProblem CheckAction(DriveAction action, DriveEntryKind kind, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(places);

        return action.KnownAction switch
        {
            DriveActionKind.Import => CheckImport(action, places),
            DriveActionKind.BackUp => kind == DriveEntryKind.CameraCards ? ActionProblem.NotForCameraCards : CheckBackUp(action, places),
            DriveActionKind.OpenFolder => IsDriveFolder(action.Folder, allowRoot: true) || FolderPaths.Normalize(action.Folder, out _) == FolderProblem.None
                ? ActionProblem.None
                : ActionProblem.InvalidFolder,
            _ => ActionProblem.UnknownAction,
        };
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is a folder on a drive without its letter, such as
    /// <c>DCIM</c> or <c>Backup\Laptop</c>: no drive, no <c>\</c> at the start or the end, no
    /// <c>.</c> or <c>..</c>, and only names Windows allows. An empty text is the drive's root,
    /// when <paramref name="allowRoot"/>.
    /// </summary>
    public static bool IsDriveFolder(string folder, bool allowRoot)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (folder.Length == 0)
        {
            return allowRoot;
        }

        if (folder.Length > MaximumDriveFolderLength || folder.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        return folder.Split('\\').All(name => name.Length > 0 && name is not ("." or "..") && FileNaming.CheckName(name) == NamingProblem.None);
    }

    /// <summary>Whether <paramref name="extension"/> is an extension without the dot, such as <c>jpg</c>.</summary>
    public static bool IsExtension(string extension) =>
        extension.Length is > 0 and <= 32 && extension.IndexOfAny(InvalidExtensionCharacters) < 0 && extension.Trim() == extension;

    private static ActionProblem CheckImport(DriveAction action, SpecialFolders places)
    {
        if (!IsDriveFolder(action.From, allowRoot: true))
        {
            return ActionProblem.InvalidDriveFolder;
        }

        if (action.Extensions.Count == 0)
        {
            return ActionProblem.NoTypes;
        }

        if (!action.Extensions.All(IsExtension))
        {
            return ActionProblem.InvalidType;
        }

        if (FolderChecks.CheckDestination(action.Destination, places) != FolderProblem.None)
        {
            return ActionProblem.DestinationNotAllowed;
        }

        return action.KnownIfExists is null ? ActionProblem.UnknownIfExists : ActionProblem.None;
    }

    private static ActionProblem CheckBackUp(DriveAction action, SpecialFolders places)
    {
        if (action.Folders.Count == 0)
        {
            return ActionProblem.NoFolders;
        }

        if (action.Folders.Count > DriveActionsSettings.MaximumFolders)
        {
            return ActionProblem.TooManyFolders;
        }

        var names = new List<string>();
        foreach (var folder in action.Folders)
        {
            if (FolderChecks.CheckFolder(folder, places) != FolderProblem.None)
            {
                return ActionProblem.FolderNotAllowed;
            }

            FolderPaths.Normalize(folder, out var fullPath);
            var name = Path.GetFileName(fullPath);
            if (name.Length == 0)
            {
                return ActionProblem.FolderNotAllowed;
            }

            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return ActionProblem.SameFolderName;
            }

            names.Add(name);
        }

        return IsDriveFolder(action.Destination, allowRoot: false) ? ActionProblem.None : ActionProblem.InvalidDriveFolder;
    }
}
