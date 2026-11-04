using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>What the feature's thread asks of the host's UI thread and of the thread itself.</summary>
public interface IDriveActionsHost
{
    /// <summary>Shows a notification. Selecting it runs <paramref name="selected"/> on the feature's thread.</summary>
    void Notify(string title, string text, Action? selected);

    /// <summary>Watches the volume with <paramref name="letter"/>, for the request to eject it and its changes, until <see cref="Unwatch"/>.</summary>
    void Watch(char letter);

    /// <summary>Stops watching the volume with <paramref name="letter"/>.</summary>
    void Unwatch(char letter);

    /// <summary>Opens a folder in File Explorer, with the user's rights, without waiting.</summary>
    void OpenFolder(string path);

    /// <summary>Gives the PC Companion icon's menu what it shows.</summary>
    void Publish(DriveActionsState state);

    /// <summary>Runs <paramref name="work"/> on the feature's thread after the messages waiting there.</summary>
    void Later(Action work);
}

/// <summary>A connected drive in the list, as the PC Companion icon's menu shows it.</summary>
/// <param name="Drive">Its number in the list.</param>
public sealed record ConnectedEntry(int Drive, string Name, char Letter);

/// <summary>A run that's going: its drive, and how far it is.</summary>
public sealed record RunProgress(char Letter, string Name, int Action, int Actions, int File, int Files);

/// <summary>What the PC Companion icon's menu shows: the connected drives with actions, and the run that's going.</summary>
public sealed record DriveActionsState(IReadOnlyList<ConnectedEntry> Connected, RunProgress? Running)
{
    public static DriveActionsState Empty { get; } = new([], null);
}

/// <summary>
/// Drive Actions' work on its own thread: it recognizes the drives that arrive, asks or acts as
/// each drive's settings say, and runs their actions one file at a time, one run at a time. See
/// docs/features/files/drive-actions.md. Call it on the feature's thread only.
/// </summary>
public sealed partial class DriveActionsRunner
{
    // A run saves what it imported and copied after this many files, besides at its end.
    private const int SaveEvery = 100;

    // Windows' error codes that say why a copy failed.
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;
    private const int AccessDenied = 5;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private const int NotReady = 21;
    private const int FileExists = 80;
    private const int AlreadyExists = 183;
    private const int DeviceNotConnected = 1167;
    private const int Cancelled = 1235;

    private readonly IDriveActionsSystem _system;
    private readonly IDriveActionsHost _host;
    private readonly DriveRecordsStore _records;
    private readonly DriveActionsHistoryStore _historyStore;
    private readonly SpecialFolders _places;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly ILogger _logger;
    private readonly Dictionary<char, Connected> _connected = [];
    private readonly Queue<char> _queue = new();
    private readonly HashSet<char> _askWhenUnlocked = [];
    private readonly HashSet<char> _watched = [];
    private DriveActionsSettings _settings = new();
    private IReadOnlyList<DriveReview> _reviews = [];
    private DriveActionsHistory? _history;
    private Run? _running;
    private bool _locked;

    public DriveActionsRunner(
        IDriveActionsSystem system,
        IDriveActionsHost host,
        DriveRecordsStore records,
        DriveActionsHistoryStore history,
        SpecialFolders places,
        TimeProvider time,
        ILogger logger,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _host = host;
        _records = records;
        _historyStore = history;
        _places = places;
        _time = time;
        _logger = logger;
        _zone = zone ?? TimeZoneInfo.Local;
    }

    /// <summary>The run that's going, whose copy the host's UI thread cancels when Windows asks to eject its drive.</summary>
    public Run? Running => _running;

    /// <summary>Applies new settings, and recognizes the connected drives again.</summary>
    public void Apply(DriveActionsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _reviews = DriveActionsCheck.Review(settings, _places);
        var usableDrives = _reviews.Count(review => review.Problem == DriveProblem.None);
        var usableActions = _reviews.Sum(review => review.Actions.Count(problem => problem == ActionProblem.None));
        LogApplied(_logger, usableDrives, usableActions);
        foreach (var letter in _connected.Keys.ToArray())
        {
            var connected = _connected[letter];
            var recognition = DriveRecognizer.Recognize(connected.Drive, _settings.Drives, _reviews);
            _connected[letter] = connected with { Recognition = recognition };
            UpdateWatch(letter);

            // A drive just added to the list, while it's connected, is asked about.
            if (recognition.Outcome != connected.Recognition.Outcome && recognition.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards)
            {
                Decide(letter, askOnly: true);
            }
        }

        Publish();
    }

    /// <summary>
    /// Looks at the drives connected now, as when the feature starts: each is asked about, also
    /// one set to act at once, since it was connected before PC Companion could see it.
    /// </summary>
    public void LookAtConnectedDrives()
    {
        var letters = _system.Letters;
        for (var index = 0; index < 26; index++)
        {
            if ((letters & (1u << index)) != 0)
            {
                Arrived((char)('A' + index), connectedBefore: true);
            }
        }
    }

    /// <summary>
    /// A volume with <paramref name="letter"/> arrived, or changed, as when it's unlocked. A drive
    /// <paramref name="connectedBefore"/> PC Companion saw it is asked about, never acted on at once.
    /// </summary>
    public void Arrived(char letter, bool connectedBefore)
    {
        var reading = _system.ReadDrive(letter);
        if (reading is null)
        {
            // A drive Windows still has, such as a locked BitLocker drive while Windows tries to
            // mount it, keeps what it was until it can be read again or goes.
            if (_connected.ContainsKey(letter) && (_system.Letters & (1u << (char.ToUpperInvariant(letter) - 'A'))) != 0)
            {
                LogNotReadable(_logger, letter);
                return;
            }

            Removed(letter);
            return;
        }

        var drive = ToConnected(letter, reading);
        var recognition = DriveRecognizer.Recognize(drive, _settings.Drives, _reviews);
        _connected.TryGetValue(letter, out var previous);
        if (recognition.Outcome == RecognitionOutcome.NotCounted)
        {
            Removed(letter);
            return;
        }

        _connected[letter] = new Connected(drive, recognition);
        UpdateWatch(letter);
        LogArrived(_logger, letter, recognition.Outcome, recognition.Drive + 1);

        // The same drive, as after a mount Windows reports again, isn't asked about twice.
        var same = previous is not null && previous.Drive.Volume == drive.Volume && previous.Recognition == recognition;
        if (!same)
        {
            Decide(letter, askOnly: connectedBefore);
        }

        Publish();
    }

    /// <summary>The volume with <paramref name="letter"/> went, ejected or pulled out.</summary>
    public void Removed(char letter)
    {
        if (_running is { } run && run.Letter == letter && !run.Cancel.IsCancellationRequested)
        {
            run.Stop(RunOutcome.Removed);
        }

        _askWhenUnlocked.Remove(letter);
        if (_connected.Remove(letter))
        {
            LogRemoved(_logger, letter);
        }

        UpdateWatch(letter);
        Publish();
    }

    /// <summary>Windows asks to eject the drive with <paramref name="letter"/>; its run, if any, has already been cancelled.</summary>
    public void Ejecting(char letter)
    {
        if (_running is { } run && run.Letter == letter)
        {
            run.Stop(RunOutcome.Ejected);
        }

        _connected.Remove(letter);
        _askWhenUnlocked.Remove(letter);
        _watched.Remove(letter);
        Publish();
    }

    /// <summary>Windows reported a change of a watched volume.</summary>
    public void VolumeChanged(char letter, VolumeChange change)
    {
        LogVolumeChanged(_logger, letter, change);
        switch (change)
        {
            case VolumeChange.Mounted or VolumeChange.EncryptionChanged:
                // Unlocked or formatted: what the drive is may have changed.
                if (_running?.Letter != letter)
                {
                    Arrived(letter, connectedBefore: false);
                }

                break;
            case VolumeChange.EjectFailed:
                // The drive stays; it's watched again.
                _watched.Remove(letter);
                if (_running?.Letter != letter)
                {
                    Arrived(letter, connectedBefore: true);
                }

                break;
            case VolumeChange.Removed:
                _watched.Remove(letter);
                Removed(letter);
                break;
        }
    }

    /// <summary>The session was locked or disconnected: nothing runs or asks until it's back.</summary>
    public void SessionLocked()
    {
        _locked = true;
        LogLocked(_logger, true);
    }

    /// <summary>The session is back: the drives that arrived meanwhile are asked about.</summary>
    public void SessionUnlocked()
    {
        _locked = false;
        LogLocked(_logger, false);
        foreach (var letter in _askWhenUnlocked.ToArray())
        {
            _askWhenUnlocked.Remove(letter);
            if (_connected.ContainsKey(letter))
            {
                Decide(letter, askOnly: true);
            }
        }
    }

    /// <summary>
    /// Runs the actions of the drive with <paramref name="letter"/>, which must still be the list's
    /// drive with <paramref name="key"/>, as when the user selects the question or Run actions.
    /// </summary>
    public void RunRequested(char letter, string key)
    {
        if (_locked)
        {
            return;
        }

        // Recognized again just before, so that a drive swapped meanwhile gets nothing.
        Arrived(letter, connectedBefore: true);
        if (_connected.TryGetValue(letter, out var connected)
            && connected.Recognition.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards
            && DriveRecordsStore.KeyOf(_settings.Drives[connected.Recognition.Drive]) == key)
        {
            Enqueue(letter);
        }
        else
        {
            LogNotRunAnyMore(_logger, letter);
        }
    }

    /// <summary>Stops the run that's going after the file it's copying.</summary>
    public void StopRun()
    {
        if (_running is { } run)
        {
            run.Stop(RunOutcome.Stopped);
        }
    }

    /// <summary>The key of the list's drive connected with <paramref name="letter"/> whose actions can run, or <see langword="null"/>.</summary>
    public string? KeyOf(char letter) =>
        _connected.TryGetValue(letter, out var connected) && connected.Recognition.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards
            ? DriveRecordsStore.KeyOf(_settings.Drives[connected.Recognition.Drive])
            : null;

    /// <summary>Stops the run that's going, as the feature stops, and saves what it did.</summary>
    public void Close()
    {
        if (_running is { } run)
        {
            run.Stop(RunOutcome.Stopped);
            Finish(run);
        }

        _queue.Clear();
        foreach (var letter in _watched.ToArray())
        {
            _host.Unwatch(letter);
        }

        _watched.Clear();
    }

    private ConnectedDrive ToConnected(char letter, DriveReading reading)
    {
        var kind = new DriveDevice(reading.Removable, reading.Fixed, reading.BusType, reading.Ejectable, reading.IsWindowsDrive).Kind;
        var root = $"{letter}:\\";
        string? idFile = null;
        var hasDcim = false;
        if (kind is not null && !reading.IsLocked)
        {
            idFile = _system.ReadIdFile(root);
            try
            {
                hasDcim = _system.Read(Path.Join(root, "DCIM")) is { IsFolder: true, IsLink: false };
            }
            catch (Win32Exception)
            {
                hasDcim = false;
            }
        }

        return new ConnectedDrive(
            reading.Volume,
            letter,
            kind,
            reading.IsLocked,
            reading.Label,
            reading.FileSystem,
            reading.Serial,
            reading.HardwareSerial,
            reading.Size,
            reading.Free,
            reading.NamedStreams,
            reading.ReadOnly,
            hasDcim,
            idFile);
    }

    // Recognized drives, drives with another's ID file and locked drives are watched, so that
    // Windows can eject them while they're in use and tell when they're unlocked.
    private void UpdateWatch(char letter)
    {
        var watch = _connected.TryGetValue(letter, out var connected)
            && connected.Recognition.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards or RecognitionOutcome.LookAlike or RecognitionOutcome.Locked;
        if (watch && _watched.Add(letter))
        {
            _host.Watch(letter);
        }
        else if (!watch && _watched.Remove(letter))
        {
            _host.Unwatch(letter);
        }
    }

    private void Decide(char letter, bool askOnly)
    {
        var connected = _connected[letter];
        switch (connected.Recognition.Outcome)
        {
            case RecognitionOutcome.LookAlike:
                var name = _settings.Drives[connected.Recognition.Drive].Name;
                LogLookAlike(_logger, letter, connected.Recognition.Drive + 1);
                _host.Notify(Format(Strings.DriveActionsLookAlikeTitle, name), Strings.DriveActionsLookAlikeText, null);
                return;
            case RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards:
                break;
            default:
                return;
        }

        var drive = _settings.Drives[connected.Recognition.Drive];
        if (!HasWork(connected.Recognition.Drive))
        {
            return;
        }

        if (_locked)
        {
            _askWhenUnlocked.Add(letter);
            return;
        }

        switch (drive.KnownWhenConnected)
        {
            case WhenConnectedChoice.Act when !askOnly:
                Enqueue(letter);
                break;
            case WhenConnectedChoice.Act or WhenConnectedChoice.Ask:
                Ask(letter, drive);
                break;
        }
    }

    private bool HasWork(int drive) =>
        _settings.Drives[drive].EjectWhenDone || _reviews[drive].Actions.Any(problem => problem == ActionProblem.None);

    private void Ask(char letter, DriveEntry drive)
    {
        var key = DriveRecordsStore.KeyOf(drive);
        LogAsking(_logger, letter);
        _host.Notify(Format(Strings.DriveActionsAskTitle, drive.Name), Strings.DriveActionsAskText, () => RunRequested(letter, key));
    }

    private void Enqueue(char letter)
    {
        if (_running?.Letter == letter || _queue.Contains(letter))
        {
            return;
        }

        _queue.Enqueue(letter);
        if (_running is null)
        {
            StartNext();
        }
    }

    private void StartNext()
    {
        while (_running is null && _queue.TryDequeue(out var letter))
        {
            if (!_connected.TryGetValue(letter, out var connected) || connected.Recognition.Outcome is not (RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards))
            {
                continue;
            }

            var index = connected.Recognition.Drive;
            var drive = _settings.Drives[index];
            var actions = drive.Actions.Where((_, action) => action < _reviews[index].Actions.Count && _reviews[index].Actions[action] == ActionProblem.None).ToArray();
            var run = new Run(letter, index, drive, connected.Drive, actions, _time.GetUtcNow());
            _running = run;
            LogRunStarted(_logger, letter, index + 1, actions.Length);
            if (drive.KnownWhenConnected == WhenConnectedChoice.Act)
            {
                _host.Notify(drive.Name, Strings.DriveActionsRunningText, null);
            }

            run.Steps = Execute(run).GetEnumerator();
            Publish();
            _host.Later(Step);
        }
    }

    private void Step()
    {
        if (_running is not { } run)
        {
            return;
        }

        if (run.Steps!.MoveNext())
        {
            Publish();
            _host.Later(Step);
            return;
        }

        Finish(run);
        StartNext();
    }

    private IEnumerable<bool> Execute(Run run)
    {
        var key = DriveRecordsStore.KeyOf(run.Entry);
        var records = _records.Load(key, out var readable);
        if (!readable)
        {
            LogRecordsUnreadable(_logger, run.Drive + 1);
        }

        for (var index = 0; index < run.Actions.Count && run.Outcome == RunOutcome.Done; index++)
        {
            run.Action = index;
            var action = run.Actions[index];
            var result = new RunAction { Action = action.Action };
            run.Results.Add(result);
            var steps = action.KnownAction switch
            {
                DriveActionKind.Import => Import(run, action, result, records),
                DriveActionKind.BackUp => BackUp(run, action, result, records),
                DriveActionKind.OpenFolder => OpenFolder(run, action, result),
                _ => [],
            };

            var copied = 0;
            foreach (var step in steps)
            {
                if (++copied % SaveEvery == 0)
                {
                    SaveRecords(key, records);
                }

                yield return step;
            }
        }

        SaveRecords(key, records);
        if (run.Outcome == RunOutcome.Done && run.Entry.EjectWhenDone)
        {
            var ejected = _system.Eject(run.Letter);
            run.Ejected = ejected.Ejected;
            LogEjected(_logger, run.Letter, ejected.Ejected, ejected.ErrorCode);
        }
    }

    private IEnumerable<bool> Import(Run run, DriveAction action, RunAction result, DriveRecords records)
    {
        var from = Path.Join(run.Root, action.From);
        IReadOnlyList<ListedFile> files;
        try
        {
            files = _system.Read(from) is { IsFolder: true, IsLink: false } ? _system.List(from) : [];
        }
        catch (Win32Exception exception)
        {
            LogNotListed(_logger, run.Drive + 1, exception.NativeErrorCode);
            files = [];
        }

        var plan = ImportPlanner.Plan(action, from, files, records, _zone, _places);
        var copies = plan.New;
        run.Files = copies.Count;
        if (!Fits(run, FolderPaths.FixedPart(action.Destination), plan.Bytes))
        {
            yield break;
        }

        var ifExists = action.KnownIfExists ?? NameConflict.AddNumber;
        FolderPaths.Normalize(FolderPaths.FixedPart(action.Destination), out var start);
        var listed = new List<RunFile>();
        result.Files = listed;
        foreach (var file in copies)
        {
            if (run.Cancel.IsCancellationRequested)
            {
                yield break;
            }

            run.File++;
            var entry = new RunFile { Path = file.RelativePath, Size = file.File.Size, Modified = file.File.Modified };
            entry = ImportFile(run, file, start, ifExists, entry);
            listed.Add(entry);
            if (entry.Outcome == FileOutcome.Imported)
            {
                result.Copied++;
                records.AddImported(new ImportedFile(file.File.Name, file.File.Size, file.File.Modified));
            }
            else
            {
                result.Skipped++;
            }

            yield return true;
        }
    }

    private RunFile ImportFile(Run run, PlannedImport file, string start, NameConflict ifExists, RunFile entry)
    {
        var problem = PrepareFolder(start, file.Destination);
        if (problem is { } skip)
        {
            return skip(entry);
        }

        foreach (var candidate in FileNaming.Candidates(file.File.Name, ifExists))
        {
            var path = Path.Join(file.Destination, candidate);
            var error = run.Copy(() => _system.Copy(file.Source, path, run.Cancel.Token));
            switch (error)
            {
                case 0:
                    return entry with { Outcome = FileOutcome.Imported, NewPath = path };
                case FileExists or AlreadyExists:
                    continue;
                default:
                    return Refused(entry, error, path);
            }
        }

        return Skipped(entry, FileSkip.NameTaken);
    }

    private IEnumerable<bool> BackUp(Run run, DriveAction action, RunAction result, DriveRecords records)
    {
        var destination = Path.Join(run.Root, action.Destination);
        var sources = new List<BackupSource>();
        var copies = new Dictionary<string, IReadOnlyList<ListedFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in action.Folders)
        {
            FolderPaths.Normalize(folder, out var path);
            var name = Path.GetFileName(path);
            try
            {
                sources.Add(new BackupSource(name, path, _system.List(path)));
            }
            catch (Win32Exception exception)
            {
                LogNotListed(_logger, run.Drive + 1, exception.NativeErrorCode);
                continue;
            }

            copies[name] = ListCopies(Path.Join(destination, name));
        }

        var plan = BackupPlanner.Plan(sources, copies, run.Connected, records, _system.HasMark);
        var toCopy = plan.ToCopy;
        run.Files = toCopy.Count;
        if (!Fits(run, run.Root, plan.Bytes))
        {
            yield break;
        }

        var skipped = new List<RunFile>();
        result.Files = skipped;
        foreach (var file in plan.Files)
        {
            if (run.Cancel.IsCancellationRequested)
            {
                yield break;
            }

            var entry = new RunFile { Path = Path.Join(file.Folder, file.RelativePath), Size = file.File.Size, Modified = file.File.Modified };
            switch (file.Step)
            {
                case BackupStep.UpToDate:
                    continue;
                case BackupStep.Adopt:
                    records.SetCopy(file.Folder, file.RelativePath, new BackupCopy(file.File.Size, file.File.Modified, file.Copy!.Size, file.Copy.Modified));
                    continue;
                case BackupStep.Skip:
                    AddSkipped(result, skipped, Skipped(entry, ToFileSkip(file.Reason)));
                    continue;
            }

            run.File++;
            entry = BackUpFile(run, destination, file, records, entry);
            if (entry.Outcome == FileOutcome.Skipped)
            {
                AddSkipped(result, skipped, entry);
            }
            else
            {
                result.Copied++;
            }

            yield return true;
        }
    }

    private RunFile BackUpFile(Run run, string destination, PlannedBackup file, DriveRecords records, RunFile entry)
    {
        // A leftover folder where the drive used to be, or another drive with its letter, gets nothing.
        if (_system.ReadDrive(run.Letter) is not { IsLocked: false } reading || reading.Serial != run.Connected.VolumeSerial)
        {
            return Skipped(entry, FileSkip.WrongDrive);
        }

        var final = Path.Join(destination, file.Folder, file.RelativePath);
        var problem = PrepareFolder(run.Root, Path.GetDirectoryName(final)!);
        if (problem is { } skip)
        {
            return skip(entry);
        }

        var temporary = final + DriveFiles.TemporarySuffix;
        var error = run.Copy(() => _system.Copy(file.Source, temporary, run.Cancel.Token));
        if (error == FileExists)
        {
            // An unfinished copy from before: it goes, and the copy starts again.
            _system.Delete(temporary);
            error = run.Copy(() => _system.Copy(file.Source, temporary, run.Cancel.Token));
        }

        if (error != 0)
        {
            return Refused(entry, error, final);
        }

        error = file.Step == BackupStep.Replace ? _system.Replace(temporary, final) : _system.Rename(temporary, final);
        if (error != 0)
        {
            _system.Delete(temporary);
            return error switch
            {
                FileExists or AlreadyExists => Skipped(entry, FileSkip.NotOurs),
                AccessDenied => Skipped(entry, FileSkip.ReadOnly),
                _ => Refused(entry, error, final),
            };
        }

        FileDetails? copy;
        try
        {
            copy = _system.Read(final);
        }
        catch (Win32Exception)
        {
            copy = null;
        }

        if (copy is not null)
        {
            records.SetCopy(file.Folder, file.RelativePath, new BackupCopy(file.File.Size, file.File.Modified, copy.Size, copy.Modified));
        }

        return entry with { Outcome = file.Step == BackupStep.Replace ? FileOutcome.Replaced : FileOutcome.Copied };
    }

    // The copies in a folder of the backup's destination, after deleting PC Companion's own
    // unfinished copies, as a drive pulled out during a copy leaves them.
    private IReadOnlyList<ListedFile> ListCopies(string folder)
    {
        IReadOnlyList<ListedFile> copies;
        try
        {
            copies = _system.Read(folder) is { IsFolder: true, IsLink: false } ? _system.List(folder) : [];
        }
        catch (Win32Exception)
        {
            return [];
        }

        var unfinished = copies.Where(copy => copy.Details.Name.EndsWith(DriveFiles.TemporarySuffix, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var copy in unfinished)
        {
            _system.Delete(Path.Join(folder, copy.RelativePath));
        }

        return unfinished.Length == 0 ? copies : [.. copies.Except(unfinished)];
    }

    private IEnumerable<bool> OpenFolder(Run run, DriveAction action, RunAction result)
    {
        var path = FolderPaths.Normalize(action.Folder, out var full) == FolderProblem.None ? full : Path.TrimEndingDirectorySeparator(Path.Join(run.Root, action.Folder));
        FileDetails? folder;
        try
        {
            folder = _system.Read(path.Length <= 3 ? run.Root : path);
        }
        catch (Win32Exception)
        {
            folder = null;
        }

        if (folder is { IsFolder: true, IsLink: false })
        {
            _host.OpenFolder(path.Length <= 3 ? run.Root : path);
            result.Opened = true;
        }

        yield break;
    }

    private bool Fits(Run run, string folder, long needed)
    {
        var free = _system.FreeSpace(folder);
        if (free < 0 || needed <= free)
        {
            return true;
        }

        LogNotEnoughSpace(_logger, run.Drive + 1);
        run.Stop(RunOutcome.NotEnoughSpace);
        (run.Needed, run.Free) = (needed, free);
        return false;
    }

    // Checks that the folder the destination starts with, start, exists, and that nothing on the
    // way is a link, and creates the subfolders after it. Returns how to skip the file, or null.
    private Func<RunFile, RunFile>? PrepareFolder(string start, string destination)
    {
        if (!FolderPaths.IsInside(destination, start))
        {
            return entry => Skipped(entry, FileSkip.DestinationNotAllowed);
        }

        var path = Path.TrimEndingDirectorySeparator(start);
        var parts = destination[path.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts.Prepend(string.Empty))
        {
            path = part.Length == 0 ? path : Path.Join(path, part);
            FileDetails? details;
            try
            {
                details = _system.Read(path.Length == 2 ? path + "\\" : path);
            }
            catch (Win32Exception exception)
            {
                var error = exception.NativeErrorCode;
                return entry => Refused(entry, error, path);
            }

            if (details is null)
            {
                if (part.Length == 0)
                {
                    return entry => Skipped(entry, FileSkip.DestinationNotAllowed);
                }

                var error = _system.CreateFolder(path);
                if (error != 0)
                {
                    return entry => Refused(entry, error, path);
                }
            }
            else if (!details.IsFolder || (details.IsLink && path.Length > 3))
            {
                return entry => Skipped(entry, FileSkip.DestinationNotAllowed);
            }
        }

        return null;
    }

    // Controlled folder access refuses apps Windows doesn't trust in the folders it protects, with
    // no error of its own, so a refusal there counts as one of its blocks, as in Folder Rules.
    private RunFile Refused(RunFile entry, int error, string path) => error switch
    {
        SharingViolation or LockViolation => Skipped(entry, FileSkip.InUse),
        Cancelled or NotReady or DeviceNotConnected => Skipped(entry, FileSkip.Interrupted),
        FileNotFound => Skipped(entry, FileSkip.Changed),
        PathNotFound => Skipped(entry, FileSkip.DestinationNotAllowed),
        AccessDenied when _places.Protected.Any(folder => FolderPaths.IsInside(path, folder)) => Skipped(entry, FileSkip.Blocked) with { ErrorCode = error },
        _ => Skipped(entry, FileSkip.Failed) with { ErrorCode = error },
    };

    private static RunFile Skipped(RunFile entry, FileSkip reason) => entry with { Outcome = FileOutcome.Skipped, Reason = reason };

    private static void AddSkipped(RunAction result, List<RunFile> skipped, RunFile entry)
    {
        result.Skipped++;
        if (skipped.Count < DriveActionsHistory.MaximumSkippedFiles)
        {
            skipped.Add(entry);
        }
    }

    private static FileSkip ToFileSkip(BackupSkip reason) => reason switch
    {
        BackupSkip.MarkWouldBeLost => FileSkip.MarkWouldBeLost,
        BackupSkip.TooLarge => FileSkip.TooLarge,
        BackupSkip.ChangedOnDrive => FileSkip.ChangedOnDrive,
        BackupSkip.NotOurs => FileSkip.NotOurs,
        BackupSkip.ReadOnly => FileSkip.ReadOnly,
        _ => FileSkip.Failed,
    };

    private void SaveRecords(string key, DriveRecords records)
    {
        try
        {
            _records.Save(key, records);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogRecordsNotSaved(_logger, exception.HResult);
        }
    }

    private void Finish(Run run)
    {
        _running = null;
        var entry = new RunEntry
        {
            Started = run.Started,
            Ended = _time.GetUtcNow(),
            Drive = run.Entry.Name,
            DriveKey = DriveRecordsStore.KeyOf(run.Entry),
            Outcome = run.Outcome,
            Needed = run.Needed,
            Free = run.Free,
            Actions = run.Results,
            Ejected = run.Ejected,
        };
        _history ??= _historyStore.Load(out _);
        _history.Add(entry, _time.GetUtcNow());
        try
        {
            _historyStore.Save(_history);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHistoryNotSaved(_logger, exception.HResult);
        }

        var copied = run.Results.Sum(result => result.Copied);
        var skipped = run.Results.Sum(result => result.Skipped);
        LogRunEnded(_logger, run.Letter, run.Outcome, copied, skipped);
        _host.Notify(run.Entry.Name, DoneText(run), null);
        Publish();
    }

    private static string DoneText(Run run)
    {
        var lines = new List<string>();
        var imported = run.Results.Where(result => result.Action.Equals(nameof(DriveActionKind.Import), StringComparison.OrdinalIgnoreCase)).Sum(result => result.Copied);
        var backedUp = run.Results.Where(result => result.Action.Equals(nameof(DriveActionKind.BackUp), StringComparison.OrdinalIgnoreCase)).Sum(result => result.Copied);
        var skipped = run.Results.Sum(result => result.Skipped);
        if (run.Results.Any(result => result.Action.Equals(nameof(DriveActionKind.Import), StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(PluralStrings.Format("DriveActionsImported", imported));
        }

        if (run.Results.Any(result => result.Action.Equals(nameof(DriveActionKind.BackUp), StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(PluralStrings.Format("DriveActionsBackedUp", backedUp));
        }

        if (skipped > 0)
        {
            lines.Add(PluralStrings.Format("DriveActionsSkipped", skipped));
        }

        lines.Add(run.Outcome switch
        {
            RunOutcome.Done => run.Ejected switch
            {
                true => Strings.DriveActionsEjected,
                false => Strings.DriveActionsNotEjected,
                null => Strings.DriveActionsCanRemove,
            },
            RunOutcome.NotEnoughSpace => Format(Strings.DriveActionsNotEnoughSpace, ByteSizes.Format(run.Needed), ByteSizes.Format(run.Free)),
            RunOutcome.Ejected or RunOutcome.Removed => Strings.DriveActionsStoppedRemoved,
            _ => Strings.DriveActionsStopped,
        });
        return string.Join(Environment.NewLine, lines);
    }

    private void Publish()
    {
        var connected = _connected
            .Where(pair => pair.Value.Recognition.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards && HasWork(pair.Value.Recognition.Drive))
            .OrderBy(pair => pair.Key)
            .Select(pair => new ConnectedEntry(pair.Value.Recognition.Drive, _settings.Drives[pair.Value.Recognition.Drive].Name, pair.Key))
            .ToArray();
        var running = _running is { } run ? new RunProgress(run.Letter, run.Entry.Name, run.Action + 1, run.Actions.Count, run.File, run.Files) : null;
        _host.Publish(new DriveActionsState(connected, running));
    }

    private static string Format(string format, params object[] values) => string.Format(System.Globalization.CultureInfo.CurrentCulture, format, values);

    private sealed record Connected(ConnectedDrive Drive, Recognition Recognition);

    /// <summary>A run of a drive's actions that's going.</summary>
    public sealed class Run(char letter, int drive, DriveEntry entry, ConnectedDrive connected, IReadOnlyList<DriveAction> actions, DateTimeOffset started)
    {
        private volatile int _outcome = (int)RunOutcome.Done;

        public char Letter { get; } = letter;

        public int Drive { get; } = drive;

        public DriveEntry Entry { get; } = entry;

        public ConnectedDrive Connected { get; } = connected;

        public string Root { get; } = $"{letter}:\\";

        public IReadOnlyList<DriveAction> Actions { get; } = actions;

        public DateTimeOffset Started { get; } = started;

        /// <summary>Cancelled when the run stops, which stops the copy that's going, from any thread.</summary>
        public CancellationTokenSource Cancel { get; } = new();

        /// <summary>Set while no copy of the run has a file open, which the host's UI thread waits for briefly when Windows asks to eject the drive.</summary>
        public ManualResetEventSlim Idle { get; } = new(initialState: true);

        public RunOutcome Outcome => (RunOutcome)_outcome;

        internal List<RunAction> Results { get; } = [];

        internal IEnumerator<bool>? Steps { get; set; }

        internal int Action { get; set; }

        internal int File { get; set; }

        internal int Files { get; set; }

        internal long Needed { get; set; }

        internal long Free { get; set; }

        internal bool? Ejected { get; set; }

        // Runs a copy, during which the run isn't idle.
        internal int Copy(Func<int> copy)
        {
            Idle.Reset();
            try
            {
                return copy();
            }
            finally
            {
                Idle.Set();
            }
        }

        /// <summary>Stops the run, from any thread, for <paramref name="outcome"/>, unless it stopped already.</summary>
        public void Stop(RunOutcome outcome)
        {
            Interlocked.CompareExchange(ref _outcome, (int)outcome, (int)RunOutcome.Done);
            try
            {
                Cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run ended meanwhile.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Drive Actions applied its settings: {Drives} drives and {Actions} actions can be used.")]
    private static partial void LogApplied(ILogger logger, int drives, int actions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "A drive arrived as {Letter}: {Outcome}, drive {Drive} in the list.")]
    private static partial void LogArrived(ILogger logger, char letter, RecognitionOutcome outcome, int drive);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The drive {Letter} can't be read now, so it stays as it was.")]
    private static partial void LogNotReadable(ILogger logger, char letter);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Windows reported a change of the drive {Letter}: {Change}.")]
    private static partial void LogVolumeChanged(ILogger logger, char letter, VolumeChange change);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The drive {Letter} went.")]
    private static partial void LogRemoved(ILogger logger, char letter);

    [LoggerMessage(Level = LogLevel.Information, Message = "The drive {Letter} has the ID file of drive {Drive} in the list, but other serial numbers, so it gets nothing.")]
    private static partial void LogLookAlike(ILogger logger, char letter, int drive);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Asking whether to run the actions of the drive {Letter}.")]
    private static partial void LogAsking(ILogger logger, char letter);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The session is locked: {Locked}.")]
    private static partial void LogLocked(ILogger logger, bool locked);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The drive {Letter} isn't the one the question was about any more, so its actions didn't run.")]
    private static partial void LogNotRunAnyMore(ILogger logger, char letter);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Running the actions of the drive {Letter}, drive {Drive} in the list: {Actions} actions.")]
    private static partial void LogRunStarted(ILogger logger, char letter, int drive, int actions);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The run for the drive {Letter} ended: {Outcome}, {Copied} files copied, {Skipped} skipped.")]
    private static partial void LogRunEnded(ILogger logger, char letter, RunOutcome outcome, int copied, int skipped);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Asked Windows to eject the drive {Letter} after its actions: ejected {Ejected} (CONFIGRET {ErrorCode}).")]
    private static partial void LogEjected(ILogger logger, char letter, bool ejected, int errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "An action of drive {Drive} in the list didn't fit on its destination's drive, so the run stopped.")]
    private static partial void LogNotEnoughSpace(ILogger logger, int drive);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A folder of an action of drive {Drive} in the list couldn't be read (error {ErrorCode}).")]
    private static partial void LogNotListed(ILogger logger, int drive, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "What drive {Drive} in the list imported and backed up couldn't be read, so it starts again.")]
    private static partial void LogRecordsUnreadable(ILogger logger, int drive);

    [LoggerMessage(Level = LogLevel.Warning, Message = "What a drive imported and backed up couldn't be saved (error {ErrorCode}).")]
    private static partial void LogRecordsNotSaved(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drive Actions' history couldn't be saved (error {ErrorCode}).")]
    private static partial void LogHistoryNotSaved(ILogger logger, int errorCode);
}
