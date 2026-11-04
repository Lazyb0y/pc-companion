using System;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Files;
using PCCompanion.Modules.Files.DriveActions;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>What the page and its dialogs say about drives and their actions.</summary>
public static class DriveActionsText
{
    private static readonly CompositeFormat DrivesTooManyFormat = CompositeFormat.Parse(Strings.DriveActionsDriveTooMany);
    private static readonly CompositeFormat ActionsTooManyFormat = CompositeFormat.Parse(Strings.DriveActionsActionTooMany);
    private static readonly CompositeFormat FoldersTooManyFormat = CompositeFormat.Parse(Strings.DriveActionsActionTooManyFolders);
    private static readonly CompositeFormat DestinationFormat = CompositeFormat.Parse(Strings.DriveActionsActionDestinationNotAllowed);
    private static readonly CompositeFormat FolderFormat = CompositeFormat.Parse(Strings.DriveActionsActionFolderNotAllowed);
    private static readonly CompositeFormat ImportFormat = CompositeFormat.Parse(Strings.DriveActionsSummaryImport);
    private static readonly CompositeFormat ImportDriveFormat = CompositeFormat.Parse(Strings.DriveActionsSummaryImportDrive);
    private static readonly CompositeFormat BackUpFormat = CompositeFormat.Parse(Strings.DriveActionsSummaryBackUp);
    private static readonly CompositeFormat OpenDriveFolderFormat = CompositeFormat.Parse(Strings.DriveActionsSummaryOpenDriveFolder);
    private static readonly CompositeFormat OpenFolderFormat = CompositeFormat.Parse(Strings.DriveActionsSummaryOpenFolder);
    private static readonly CompositeFormat ProblemFormat = CompositeFormat.Parse(Strings.DriveActionsSummaryProblem);

    /// <summary>What keeps a drive from being used, or an empty string.</summary>
    public static string Of(DriveProblem problem) => problem switch
    {
        DriveProblem.NoName => Strings.DriveActionsDriveNoName,
        DriveProblem.NameTooLong => Strings.DriveActionsDriveNameTooLong,
        DriveProblem.SameName => Strings.DriveActionsDriveSameName,
        DriveProblem.UnknownKind => Strings.DriveActionsDriveUnknownKind,
        DriveProblem.UnknownWhenConnected => Strings.DriveActionsDriveUnknownWhenConnected,
        DriveProblem.NotAdded => Strings.DriveActionsDriveNotAdded,
        DriveProblem.SecondCameraCards => Strings.DriveActionsDriveSecondCameraCards,
        DriveProblem.TooMany => string.Format(CultureInfo.CurrentCulture, DrivesTooManyFormat, DriveActionsSettings.MaximumDrives),
        _ => string.Empty,
    };

    /// <summary>What keeps an action from being used, or an empty string.</summary>
    public static string Of(ActionProblem problem, DriveAction action, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(places);

        return problem switch
        {
            ActionProblem.UnknownAction => Strings.DriveActionsActionUnknown,
            ActionProblem.InvalidDriveFolder => Strings.DriveActionsActionInvalidDriveFolder,
            ActionProblem.NoTypes => Strings.DriveActionsActionNoTypes,
            ActionProblem.InvalidType => Strings.DriveActionsActionInvalidType,
            ActionProblem.DestinationNotAllowed => string.Format(CultureInfo.CurrentCulture, DestinationFormat, Of(FolderChecks.CheckDestination(action.Destination, places))),
            ActionProblem.UnknownIfExists => Strings.DriveActionsActionUnknownIfExists,
            ActionProblem.NotForCameraCards => Strings.DriveActionsActionNotForCameraCards,
            ActionProblem.NoFolders => Strings.DriveActionsActionNoFolders,
            ActionProblem.TooManyFolders => string.Format(CultureInfo.CurrentCulture, FoldersTooManyFormat, DriveActionsSettings.MaximumFolders),
            ActionProblem.FolderNotAllowed => string.Format(CultureInfo.CurrentCulture, FolderFormat, FolderReason(action, places)),
            ActionProblem.SameFolderName => Strings.DriveActionsActionSameFolderName,
            ActionProblem.InvalidFolder => Strings.DriveActionsActionInvalidFolder,
            ActionProblem.TooMany => string.Format(CultureInfo.CurrentCulture, ActionsTooManyFormat, DriveActionsSettings.MaximumActions),
            _ => string.Empty,
        };
    }

    /// <summary>Why a folder on the PC can't be imported into or backed up, or an empty string.</summary>
    public static string Of(FolderProblem problem) => problem switch
    {
        FolderProblem.NoPath => Strings.DriveActionsFolderNoPath,
        FolderProblem.InvalidPath => Strings.DriveActionsFolderInvalidPath,
        FolderProblem.NotFullPath => Strings.DriveActionsFolderNotFullPath,
        FolderProblem.WindowsFolder => Strings.DriveActionsFolderWindows,
        FolderProblem.ProgramFiles => Strings.DriveActionsFolderProgramFiles,
        FolderProblem.DataFolder => Strings.DriveActionsFolderDataFolder,
        FolderProblem.SystemFolder => Strings.DriveActionsFolderSystem,
        FolderProblem.InvalidPart => Strings.DriveActionsFolderInvalidPart,
        _ => string.Empty,
    };

    /// <summary>Why a drive connected now can't be added to the list, or an empty string.</summary>
    public static string Of(AddProblem problem) => problem switch
    {
        AddProblem.Locked => Strings.DriveActionsAddLocked,
        AddProblem.ReadOnly => Strings.DriveActionsAddReadOnly,
        AddProblem.AlreadyAdded => Strings.DriveActionsAddAlreadyAdded,
        AddProblem.LookAlike => Strings.DriveActionsAddLookAlike,
        AddProblem.NotCounted => Strings.DriveActionsAddNotCounted,
        _ => string.Empty,
    };

    /// <summary>A connected drive's kind, as Add a drive shows it, and the name of one without a label.</summary>
    public static string Of(ConnectedDriveKind? kind) => kind switch
    {
        ConnectedDriveKind.MemoryCard => Strings.DriveActionsKindMemoryCard,
        ConnectedDriveKind.ExternalDisk => Strings.DriveActionsKindExternalDisk,
        ConnectedDriveKind.VirtualDisk => Strings.DriveActionsKindVirtualDisk,
        _ => Strings.DriveActionsKindUsbDrive,
    };

    /// <summary>What an action is, as its card's header.</summary>
    public static string NameOf(DriveAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action.KnownAction switch
        {
            DriveActionKind.Import => Strings.DriveActionsActionImport,
            DriveActionKind.BackUp => Strings.DriveActionsActionBackUp,
            DriveActionKind.OpenFolder => Strings.DriveActionsActionOpenFolder,
            _ => Strings.DriveActionsActionOther,
        };
    }

    /// <summary>What an action does, or why it isn't used.</summary>
    public static string Summary(DriveAction action, string problem)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(problem);

        if (problem.Length > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, ProblemFormat, problem);
        }

        return action.KnownAction switch
        {
            DriveActionKind.Import when action.From.Length == 0 => string.Format(CultureInfo.CurrentCulture, ImportDriveFormat, action.Destination.Trim()),
            DriveActionKind.Import => string.Format(CultureInfo.CurrentCulture, ImportFormat, action.From, action.Destination.Trim()),
            DriveActionKind.BackUp => string.Format(
                CultureInfo.CurrentCulture,
                BackUpFormat,
                string.Join(CultureInfo.CurrentCulture.TextInfo.ListSeparator + " ", action.Folders.Select(FolderName)),
                action.Destination),
            _ when action.Folder.Length == 0 => Strings.DriveActionsSummaryOpenDrive,
            _ when DriveActionsCheck.IsDriveFolder(action.Folder, allowRoot: false) => string.Format(CultureInfo.CurrentCulture, OpenDriveFolderFormat, action.Folder),
            _ => string.Format(CultureInfo.CurrentCulture, OpenFolderFormat, action.Folder),
        };
    }

    /// <summary>A drive's letter as the page shows it, such as <c>E:</c>.</summary>
    public static string Letter(string letter) => letter + ":";

    private static string FolderName(string path)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path.Trim()));
        return name.Length > 0 ? name : path.Trim();
    }

    // The first folder of the backup that can't be used and why, or the drive's root, which can't be.
    private static string FolderReason(DriveAction action, SpecialFolders places)
    {
        foreach (var folder in action.Folders)
        {
            var problem = FolderChecks.CheckFolder(folder, places);
            if (problem != FolderProblem.None)
            {
                return Of(problem);
            }
        }

        return Strings.DriveActionsFolderRoot;
    }
}
