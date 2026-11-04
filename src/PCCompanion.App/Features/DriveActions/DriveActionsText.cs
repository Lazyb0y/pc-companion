using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PCCompanion.Localization;
using PCCompanion.Modules.Files;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;

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
    private static readonly CompositeFormat NotEnoughSpaceFormat = CompositeFormat.Parse(Strings.DriveActionsNotEnoughSpace);
    private static readonly CompositeFormat FileImportedFormat = CompositeFormat.Parse(Strings.DriveActionsFileImported);
    private static readonly CompositeFormat FileSkippedFormat = CompositeFormat.Parse(Strings.DriveActionsFileSkipped);
    private static readonly CompositeFormat FileUndoneFormat = CompositeFormat.Parse(Strings.DriveActionsFileUndone);
    private static readonly CompositeFormat ReasonFailedFormat = CompositeFormat.Parse(Strings.DriveActionsReasonFailed);

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

    /// <summary>What a run's actions did, and how it ended.</summary>
    public static string Of(RunEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var lines = new List<string>();
        foreach (var action in run.Actions)
        {
            var kind = new DriveAction { Action = action.Action }.KnownAction;
            if (kind == DriveActionKind.Import)
            {
                lines.Add(PluralStrings.Format("DriveActionsImported", action.Copied));
            }
            else if (kind == DriveActionKind.BackUp)
            {
                lines.Add(PluralStrings.Format("DriveActionsBackedUp", action.Copied));
            }
            else if (kind == DriveActionKind.OpenFolder)
            {
                lines.Add(action.Opened ? Strings.DriveActionsHistoryOpened : Strings.DriveActionsHistoryNotOpened);
            }
        }

        var skipped = run.Actions.Sum(action => action.Skipped);
        if (skipped > 0)
        {
            lines.Add(PluralStrings.Format("DriveActionsSkipped", skipped));
        }

        lines.Add(run.Outcome switch
        {
            RunOutcome.Done => run.Ejected switch
            {
                true => Strings.DriveActionsHistoryEjected,
                false => Strings.DriveActionsNotEjected,
                null => Strings.DriveActionsHistoryDone,
            },
            RunOutcome.NotEnoughSpace => string.Format(CultureInfo.CurrentCulture, NotEnoughSpaceFormat, ByteSizes.Format(run.Needed), ByteSizes.Format(run.Free)),
            RunOutcome.Ejected or RunOutcome.Removed => Strings.DriveActionsStoppedRemoved,
            _ => Strings.DriveActionsStopped,
        });
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>What happened to a file of a run, and whether Undo put it back.</summary>
    public static string Of(RunFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var what = file.Outcome switch
        {
            FileOutcome.Imported => string.Format(CultureInfo.CurrentCulture, FileImportedFormat, file.NewPath),
            FileOutcome.Copied => Strings.DriveActionsFileCopied,
            FileOutcome.Replaced => Strings.DriveActionsFileReplaced,
            _ => string.Format(CultureInfo.CurrentCulture, FileSkippedFormat, Of(file.Reason, file.ErrorCode)),
        };

        return file.Undone ? string.Format(CultureInfo.CurrentCulture, FileUndoneFormat, what) : what;
    }

    private static string Of(FileSkip reason, int errorCode) => reason switch
    {
        FileSkip.NameTaken => Strings.DriveActionsReasonNameTaken,
        FileSkip.InUse => Strings.DriveActionsReasonInUse,
        FileSkip.Blocked => Strings.DriveActionsReasonBlocked,
        FileSkip.DestinationNotAllowed => Strings.DriveActionsReasonDestinationNotAllowed,
        FileSkip.Interrupted => Strings.DriveActionsReasonInterrupted,
        FileSkip.Changed => Strings.DriveActionsReasonChanged,
        FileSkip.MarkWouldBeLost => Strings.DriveActionsReasonMarkWouldBeLost,
        FileSkip.TooLarge => Strings.DriveActionsReasonTooLarge,
        FileSkip.ChangedOnDrive => Strings.DriveActionsReasonChangedOnDrive,
        FileSkip.NotOurs => Strings.DriveActionsReasonNotOurs,
        FileSkip.ReadOnly => Strings.DriveActionsReasonReadOnly,
        FileSkip.WrongDrive => Strings.DriveActionsReasonWrongDrive,
        _ => string.Format(CultureInfo.CurrentCulture, ReasonFailedFormat, errorCode),
    };

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
