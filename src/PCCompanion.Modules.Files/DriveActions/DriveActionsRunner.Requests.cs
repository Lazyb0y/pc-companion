using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.DriveActions;

// The settings app's requests, which the pipe passes to the feature's thread. The host checks each
// one: a drive must be connected, the same volume and the list's drive the request names, and an
// entry to undo must be in its history, so a request can't make it touch other drives or files.
public sealed partial class DriveActionsRunner
{
    private readonly Dictionary<char, IReadOnlyList<ChosenImport>> _chosen = [];
    private IRecycleBin? _recycleBin;

    /// <summary>Answers a request from the settings app, at once or, for Undo, once the Recycle Bin is done.</summary>
    public void Answer(DriveActionsRequest request, Action<DriveActionsAnswer> reply)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reply);

        LogRequest(_logger, request.Kind);
        switch (request.Kind)
        {
            case DriveActionsRequestKind.Drives:
                reply(new DriveActionsAnswer { Drives = [.. _connected.OrderBy(pair => pair.Key).Select(pair => Describe(pair.Key, pair.Value))], Running = RunningProgress() });
                break;
            case DriveActionsRequestKind.AddDrive:
                reply(AddDrive(request));
                break;
            case DriveActionsRequestKind.UseDrive:
                reply(UseDrive(request));
                break;
            case DriveActionsRequestKind.History:
                _history ??= _historyStore.Load(out _);
                reply(new DriveActionsAnswer { Runs = [.. _history.Runs.Reverse()], Running = RunningProgress() });
                break;
            case DriveActionsRequestKind.ClearHistory:
                _history ??= _historyStore.Load(out _);
                _history.Clear();
                SaveHistory();
                reply(new DriveActionsAnswer());
                break;
            case DriveActionsRequestKind.RunNowList:
                reply(RunNowList(request));
                break;
            case DriveActionsRequestKind.RunNow:
                reply(RunNow(request));
                break;
            case DriveActionsRequestKind.Stop:
                StopRun();
                reply(new DriveActionsAnswer());
                break;
            case DriveActionsRequestKind.Undo:
                Undo(request, reply);
                break;
            default:
                reply(Answered(DriveActionsAnswerStatus.Refused));
                break;
        }
    }

    private ConnectedDriveInfo Describe(char letter, Connected connected) => new()
    {
        Letter = letter.ToString(),
        Volume = connected.Drive.Volume,
        Label = connected.Drive.Label,
        FileSystem = connected.Drive.FileSystem,
        Size = connected.Drive.Size,
        Free = connected.Drive.Free,
        Kind = connected.Drive.Kind,
        IsLocked = connected.Drive.IsLocked,
        KeepsStreams = connected.Drive.KeepsStreams,
        Outcome = connected.Recognition.Outcome,
        Drive = connected.Recognition.Drive,
        AddProblem = DriveRecognizer.CheckAdd(connected.Drive, _settings.Drives, _reviews),
    };

    private DriveActionsAnswer AddDrive(DriveActionsRequest request)
    {
        if (!TryConnected(request, out var letter, out var connected))
        {
            return Answered(DriveActionsAnswerStatus.NotConnected);
        }

        var problem = DriveRecognizer.CheckAdd(connected.Drive, _settings.Drives, _reviews);
        if (problem != AddProblem.None)
        {
            return new DriveActionsAnswer { Status = DriveActionsAnswerStatus.CantAdd, AddProblem = problem };
        }

        // A drive added on another PC keeps the ID file it has.
        var id = connected.Drive.IdFileText is { } text ? DriveIdentity.ReadId(text) : null;
        if (id is null)
        {
            id = DriveIdentity.NewId();
            var error = _system.WriteIdFile(connected.Drive.Root, id);
            if (error != 0)
            {
                LogIdFileNotWritten(_logger, letter, error);
                return new DriveActionsAnswer { Status = DriveActionsAnswerStatus.Failed, ErrorCode = error };
            }
        }

        // Named after its label; the settings app's name is for a drive without one.
        LogAdded(_logger, letter);
        var name = request.Name.Trim();
        name = name.Length == 0 ? $"{letter}:" : name[..Math.Min(name.Length, DriveActionsSettings.MaximumNameLength)];
        return new DriveActionsAnswer { Entry = DriveRecognizer.NewEntry(connected.Drive, id, name) };
    }

    private DriveActionsAnswer UseDrive(DriveActionsRequest request)
    {
        if (!TryConnected(request, out _, out var connected))
        {
            return Answered(DriveActionsAnswerStatus.NotConnected);
        }

        if (connected.Recognition.Outcome != RecognitionOutcome.LookAlike || DriveRecordsStore.KeyOf(_settings.Drives[connected.Recognition.Drive]) != request.Key)
        {
            return Answered(DriveActionsAnswerStatus.Refused);
        }

        return new DriveActionsAnswer { Entry = DriveRecognizer.WithSerialsOf(_settings.Drives[connected.Recognition.Drive], connected.Drive) };
    }

    private DriveActionsAnswer RunNowList(DriveActionsRequest request)
    {
        if (!TryRecognized(request, out _, out var connected))
        {
            return Answered(DriveActionsAnswerStatus.NotConnected);
        }

        var index = connected.Recognition.Drive;
        var drive = _settings.Drives[index];
        var records = _records.Load(DriveRecordsStore.KeyOf(drive), out _);
        var previews = new List<ActionPreview>();
        for (var action = 0; action < drive.Actions.Count && action < _reviews[index].Actions.Count; action++)
        {
            if (_reviews[index].Actions[action] != ActionProblem.None)
            {
                continue;
            }

            switch (drive.Actions[action].KnownAction)
            {
                case DriveActionKind.Import:
                    var plan = PlanImport(connected.Drive.Root, drive.Actions[action], records);
                    previews.Add(new ActionPreview
                    {
                        Action = action,
                        Files = [.. plan.Files.Select(file => new PreviewFile { Path = file.RelativePath, Size = file.File.Size, Modified = file.File.Modified, Destination = file.Destination, ImportedBefore = file.ImportedBefore })],
                    });
                    break;
                case DriveActionKind.BackUp:
                    var backup = PlanBackUp(connected.Drive, drive.Actions[action], records, deleteUnfinished: false);
                    previews.Add(new ActionPreview { Action = action, Copies = backup.ToCopy.Count, Bytes = backup.Bytes, Skipped = backup.Files.Count(file => file.Step == BackupStep.Skip) });
                    break;
            }
        }

        return new DriveActionsAnswer { Actions = previews };
    }

    private DriveActionsAnswer RunNow(DriveActionsRequest request)
    {
        if (_locked)
        {
            return Answered(DriveActionsAnswerStatus.Refused);
        }

        if (!TryRecognized(request, out var letter, out _))
        {
            return Answered(DriveActionsAnswerStatus.NotConnected);
        }

        if (_running?.Letter == letter || _queue.Contains(letter))
        {
            return Answered(DriveActionsAnswerStatus.Refused);
        }

        _chosen[letter] = request.Chosen;
        Enqueue(letter);
        return new DriveActionsAnswer();
    }

    // Undo sends the imported copies, unchanged since, to the Recycle Bin, one after the other on its
    // own thread, and replies once all are done. An undone file still counts as imported.
    private void Undo(DriveActionsRequest request, Action<DriveActionsAnswer> reply)
    {
        _history ??= _historyStore.Load(out _);
        if (_history.Find(request.Run) is not { } run || run.Started != request.Started)
        {
            reply(Answered(DriveActionsAnswerStatus.Refused));
            return;
        }

        var files = run.Actions
            .SelectMany((action, index) => action.Files.Select(file => (Action: index, File: file)))
            .Where(item => item.File is { Outcome: FileOutcome.Imported, Undone: false, NewPath: not null } && (request.File.Length == 0 || item.File.Path == request.File))
            .ToArray();
        if (files.Length == 0)
        {
            reply(Answered(DriveActionsAnswerStatus.Refused));
            return;
        }

        var undone = new List<(int Action, RunFile File)>();
        var notUndone = 0;
        var waiting = files.Length;
        void Finished()
        {
            if (--waiting > 0)
            {
                return;
            }

            if (undone.Count > 0 && _history.Find(run.Id) is { } current)
            {
                var actions = current.Actions.Select((action, index) => action with
                {
                    Files = [.. action.Files.Select(file => undone.Contains((index, file)) ? file with { Undone = true } : file)],
                }).ToArray();
                _history.Replace(current with { Actions = actions });
                SaveHistory();
            }

            LogUndone(_logger, undone.Count, notUndone);
            reply(new DriveActionsAnswer { Undone = undone.Count, NotUndone = notUndone });
        }

        foreach (var (action, file) in files)
        {
            if (!IsUnchanged(file))
            {
                notUndone++;
                Finished();
                continue;
            }

            _recycleBin ??= _system.CreateRecycleBin();
            _recycleBin.Recycle(file.NewPath!, () => { }, result => _host.Later(() =>
            {
                if (result.Outcome == RecycleOutcome.Recycled)
                {
                    undone.Add((action, file));
                }
                else
                {
                    notUndone++;
                }

                Finished();
            }));
        }
    }

    // The copy is still where the import put it, with the size and time it had.
    private bool IsUnchanged(RunFile file)
    {
        try
        {
            return _system.Read(file.NewPath!) is { IsFolder: false, IsLink: false } copy && copy.Size == file.Size && FileTimes.AreSame(copy.Modified, file.Modified);
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private bool TryConnected(DriveActionsRequest request, out char letter, out Connected connected)
    {
        letter = request.Letter.Length == 1 ? char.ToUpperInvariant(request.Letter[0]) : '\0';
        if (_connected.TryGetValue(letter, out var found) && found.Drive.Volume == request.Volume && !found.Drive.IsLocked)
        {
            connected = found;
            return true;
        }

        connected = null!;
        return false;
    }

    private bool TryRecognized(DriveActionsRequest request, out char letter, out Connected connected) =>
        TryConnected(request, out letter, out connected)
        && connected.Recognition.Outcome is RecognitionOutcome.Recognized or RecognitionOutcome.CameraCards
        && DriveRecordsStore.KeyOf(_settings.Drives[connected.Recognition.Drive]) == request.Key;

    private RunProgress? RunningProgress() =>
        _running is { } run ? new RunProgress(run.Letter, run.Entry.Name, run.Action + 1, run.Actions.Count, run.File, run.Files) : null;

    private static DriveActionsAnswer Answered(DriveActionsAnswerStatus status) => new() { Status = status };

    [LoggerMessage(Level = LogLevel.Debug, Message = "The settings app asked for {Kind}.")]
    private static partial void LogRequest(ILogger logger, DriveActionsRequestKind kind);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The drive {Letter} was added to the list.")]
    private static partial void LogAdded(ILogger logger, char letter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ID file couldn't be written to the drive {Letter} (error {ErrorCode}).")]
    private static partial void LogIdFileNotWritten(ILogger logger, char letter, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Undo sent {Undone} copies to the Recycle Bin; {NotUndone} stayed.")]
    private static partial void LogUndone(ILogger logger, int undone, int notUndone);
}
