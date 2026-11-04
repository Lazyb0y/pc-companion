using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>A file found in a folder or its subfolders, with its path in that folder, such as <c>100CANON\IMG_0001.JPG</c>.</summary>
public sealed record ListedFile(string RelativePath, FileDetails Details);

/// <summary>The files and folders actions leave alone.</summary>
public static class DriveFiles
{
    /// <summary>What a new copy on a drive is called until it takes the old copy's place: the copy's name and this.</summary>
    public const string TemporarySuffix = ".pccompanion-new";

    /// <summary>
    /// Whether actions leave a file alone: a folder, a link, a file kept online only, which reading
    /// would download, a hidden or system file, or an unfinished copy of PC Companion's.
    /// </summary>
    public static bool IsLeftAlone(FileDetails file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.IsFolder || file.IsLink || file.IsOnlineOnly
            || (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0
            || file.Name.EndsWith(TemporarySuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a listing goes into a folder: not a link, and neither hidden nor system.</summary>
    public static bool IsListed(FileDetails folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return folder.IsFolder && !folder.IsLink && (folder.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
    }
}

/// <summary>A file an import would copy, and where to.</summary>
/// <param name="Source">The file's full path on the drive.</param>
/// <param name="Destination">The folder it goes to, with the subfolders made from it.</param>
/// <param name="ImportedBefore">A file with its name, size and time was imported before, so it's copied only when chosen in Run now.</param>
public sealed record PlannedImport(string Source, string RelativePath, FileFacts File, string Destination, bool ImportedBefore);

/// <summary>What an import would copy.</summary>
public sealed record ImportPlan(IReadOnlyList<PlannedImport> Files)
{
    /// <summary>The files it copies unless chosen otherwise: those not imported before.</summary>
    public IReadOnlyList<PlannedImport> New => [.. Files.Where(file => !file.ImportedBefore)];

    /// <summary>The space the new files need.</summary>
    public long Bytes => New.Sum(file => file.File.Size);
}

/// <summary>Works out what an import copies. See "Import from the drive" in docs/features/files/drive-actions.md.</summary>
public static class ImportPlanner
{
    /// <param name="from">The full path of the folder on the drive the import starts from.</param>
    /// <param name="files">The files in it and its subfolders.</param>
    public static ImportPlan Plan(DriveAction action, string from, IReadOnlyList<ListedFile> files, DriveRecords records, TimeZoneInfo zone, SpecialFolders places)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(records);

        var extensions = new HashSet<string>(action.Extensions, StringComparer.OrdinalIgnoreCase);
        var planned = new List<PlannedImport>();
        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var details = file.Details;
            if (DriveFiles.IsLeftAlone(details) || details.Size == 0 || FileNaming.CheckName(details.Name) != NamingProblem.None)
            {
                continue;
            }

            var facts = new FileFacts(details.Name, details.Size, details.Created, details.Modified);
            if (!extensions.Contains(facts.Extension))
            {
                continue;
            }

            var source = Path.Join(from, file.RelativePath);
            var destination = FileNaming.Destination(action.Destination, facts, zone, Path.GetDirectoryName(source)!, places);
            if (destination.Problem != NamingProblem.None)
            {
                continue;
            }

            planned.Add(new PlannedImport(source, file.RelativePath, facts, destination.Value, records.WasImported(details.Name, details.Size, details.Modified)));
        }

        return new ImportPlan(planned);
    }
}

/// <summary>What a backup does with a file of the PC.</summary>
public enum BackupStep
{
    /// <summary>Copies it: the drive has no copy.</summary>
    Copy,

    /// <summary>Replaces PC Companion's own copy, unchanged on the drive, with one of the changed file.</summary>
    Replace,

    /// <summary>Records the drive's copy as its own: it's the same as the file, as after a reinstall.</summary>
    Adopt,

    /// <summary>Nothing: the copy is up to date.</summary>
    UpToDate,

    /// <summary>Leaves it, for <see cref="PlannedBackup.Reason"/>.</summary>
    Skip,
}

/// <summary>Why a backup leaves a file.</summary>
public enum BackupSkip
{
    None,

    /// <summary>A file with a Mark of the Web, for a drive that can't keep it.</summary>
    MarkWouldBeLost,

    /// <summary>A file too large for the drive's file system, such as one of 4 GB or more on FAT32.</summary>
    TooLarge,

    /// <summary>PC Companion's copy was changed on the drive.</summary>
    ChangedOnDrive,

    /// <summary>The drive has a file of that name that PC Companion didn't put there.</summary>
    NotOurs,

    /// <summary>The copy to replace is read-only, which Windows doesn't replace.</summary>
    ReadOnly,
}

/// <summary>A file of a backup's folder, and what the backup does with it.</summary>
/// <param name="Folder">The name of the folder in the backup's destination, such as <c>Documents</c>.</param>
/// <param name="Source">The file's full path on the PC.</param>
/// <param name="Copy">The drive's copy, if there is one.</param>
public sealed record PlannedBackup(string Folder, string RelativePath, string Source, FileDetails File, FileDetails? Copy, BackupStep Step, BackupSkip Reason = BackupSkip.None);

/// <summary>What a backup does with each file.</summary>
public sealed record BackupPlan(IReadOnlyList<PlannedBackup> Files)
{
    /// <summary>The space the copies need: every new copy, and every changed file's, before its old copy goes.</summary>
    public long Bytes => Files.Where(file => file.Step is BackupStep.Copy or BackupStep.Replace).Sum(file => file.File.Size);

    /// <summary>The files it copies or replaces.</summary>
    public IReadOnlyList<PlannedBackup> ToCopy => [.. Files.Where(file => file.Step is BackupStep.Copy or BackupStep.Replace)];
}

/// <summary>One of a backup's folders: its name in the destination, its full path and its files.</summary>
public sealed record BackupSource(string Folder, string Path, IReadOnlyList<ListedFile> Files);

/// <summary>Works out what a backup copies. See "Back up to the drive" in docs/features/files/drive-actions.md.</summary>
public static class BackupPlanner
{
    /// <param name="sources">The backup's folders on the PC, with their files.</param>
    /// <param name="copies">The files in each folder of the destination on the drive, by the folder's name.</param>
    /// <param name="hasMark">Whether a file of the PC has a Mark of the Web; asked only for a drive that can't keep it.</param>
    public static BackupPlan Plan(
        IReadOnlyList<BackupSource> sources,
        IReadOnlyDictionary<string, IReadOnlyList<ListedFile>> copies,
        ConnectedDrive drive,
        DriveRecords records,
        Func<string, bool> hasMark)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(copies);
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(hasMark);

        var planned = new List<PlannedBackup>();
        foreach (var source in sources)
        {
            var onDrive = (copies.TryGetValue(source.Folder, out var listed) ? listed : [])
                .GroupBy(copy => copy.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Details, StringComparer.OrdinalIgnoreCase);
            foreach (var file in source.Files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (DriveFiles.IsLeftAlone(file.Details))
                {
                    continue;
                }

                var path = System.IO.Path.Join(source.Path, file.RelativePath);
                onDrive.TryGetValue(file.RelativePath, out var copy);
                var step = Decide(file.Details, copy, records.CopyOf(source.Folder, file.RelativePath), out var reason);
                if (step is BackupStep.Copy or BackupStep.Replace)
                {
                    if (file.Details.Size > drive.MaximumFileSize)
                    {
                        (step, reason) = (BackupStep.Skip, BackupSkip.TooLarge);
                    }
                    else if (!drive.KeepsStreams && hasMark(path))
                    {
                        (step, reason) = (BackupStep.Skip, BackupSkip.MarkWouldBeLost);
                    }
                }

                planned.Add(new PlannedBackup(source.Folder, file.RelativePath, path, file.Details, copy, step, reason));
            }
        }

        return new BackupPlan(planned);
    }

    /// <summary>Whether the drive's copy is still as PC Companion left it.</summary>
    public static bool IsUnchanged(FileDetails copy, BackupCopy recorded)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(recorded);

        return copy.Size == recorded.CopySize && FileTimes.AreSame(copy.Modified, recorded.CopyModified);
    }

    private static BackupStep Decide(FileDetails file, FileDetails? copy, BackupCopy? recorded, out BackupSkip reason)
    {
        reason = BackupSkip.None;
        if (copy is null)
        {
            return BackupStep.Copy;
        }

        if (copy.IsFolder || copy.IsLink)
        {
            reason = BackupSkip.NotOurs;
            return BackupStep.Skip;
        }

        if (recorded is not null && IsUnchanged(copy, recorded))
        {
            if (file.Size == recorded.SourceSize && file.Modified == recorded.SourceModified)
            {
                return BackupStep.UpToDate;
            }

            if (copy.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                reason = BackupSkip.ReadOnly;
                return BackupStep.Skip;
            }

            return BackupStep.Replace;
        }

        // The same file, as when PC Companion's records were lost with its data folder.
        if (copy.Size == file.Size && FileTimes.AreSame(copy.Modified, file.Modified))
        {
            return BackupStep.Adopt;
        }

        reason = recorded is null ? BackupSkip.NotOurs : BackupSkip.ChangedOnDrive;
        return BackupStep.Skip;
    }
}
