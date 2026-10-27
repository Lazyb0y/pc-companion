using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.FolderRules;

/// <summary>
/// Runs Folder Rules on a <see cref="MessageLoopThread{TSettings}"/> of its own, at background
/// priority, as "Threads" in docs/features/files/folder-rules.md says: it watches the folders,
/// waits for each file to finish arriving, and handles the files one at a time, in the order they
/// arrived. The host's UI thread starts it, passes it new settings and stops it, and never waits
/// for it, except for <see cref="MessageLoopThread{TSettings}.Join"/> when the host exits.
/// </summary>
public sealed partial class FolderRulesThread : MessageLoopThread<FolderRulesSettings>
{
    private const string WindowClassName = "PCCompanion.FolderRules";

    // How often the files that wait to finish arriving are checked.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    private readonly IFolderRulesSystem _system;
    private readonly HistoryStore _store;
    private readonly SpecialFolders _places;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    // Cancelled as the thread stops, which stops a copy that's running. Nothing waits on it, so
    // it holds nothing to dispose.
    private readonly CancellationTokenSource _stopping;

    private Running? _running;

    /// <summary>Starts the thread, which watches the folders in <paramref name="settings"/>.</summary>
    /// <param name="previous">A thread that was asked to stop, which this one waits for first, so that they don't share the history.</param>
    public FolderRulesThread(
        FolderRulesSettings settings, IFolderRulesSystem system, HistoryStore store, SpecialFolders places, TimeProvider time, ILogger logger, FolderRulesThread? previous = null)
        : base("Folder Rules", WindowClassName, settings, previous)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _system = system;
        _store = store;
        _places = places;
        _time = time;
        _logger = logger;
        var stopping = new CancellationTokenSource();
        _stopping = stopping;
        Start();
    }

    /// <summary>
    /// Stops a move or copy that's running, from the thread that stops this one, which a request
    /// to the thread would reach only once the copy is done. The file stays where it was.
    /// </summary>
    public void CancelWork() => _stopping.Cancel();

    protected override IDisposable? Open(MessageWindow window)
    {
        _system.EnterBackgroundMode();
        var running = new Running(this, window);
        _running = running;
        return running;
    }

    protected override void Apply(FolderRulesSettings settings) => _running!.Apply(settings);

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopped() => LogStopped(_logger);

    protected override void OnFailed(Exception exception) => LogFailed(_logger, exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules started on its own thread.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules' thread ended.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Folder Rules failed, so it's stopped until PC Companion restarts.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules applied: {Folders} folders and {Rules} rules can be used.")]
    private static partial void LogApplied(ILogger logger, int folders, int rules);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules folder {Folder} can't be used: {Problem}.")]
    private static partial void LogUnusableFolder(ILogger logger, int folder, FolderProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules folder {Folder}, rule {Rule} can't be used: {Problem}.")]
    private static partial void LogUnusableRule(ILogger logger, int folder, int rule, RuleProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules has {Count} folders and watches the first {Maximum}.")]
    private static partial void LogTooManyFolders(ILogger logger, int count, int maximum);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules has {Count} rules and uses the first {Maximum}.")]
    private static partial void LogTooManyRules(ILogger logger, int count, int maximum);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules can't watch folder {Folder} (error {ErrorCode}).")]
    private static partial void LogNotWatched(ILogger logger, int folder, int errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules can't list folder {Folder} (error {ErrorCode}).")]
    private static partial void LogNotListed(ILogger logger, int folder, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules' watch of folder {Folder} stopped (lost changes: {LostChanges}), so it starts again and looks at the whole folder.")]
    private static partial void LogWatchFailed(ILogger logger, int folder, bool lostChanges);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules looked at folder {Folder}: {Arrived} of {Files} files arrived since it last looked.")]
    private static partial void LogLooked(ILogger logger, int folder, int arrived, int files);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules folder {Folder}, rule {Rule}: {Outcome}.")]
    private static partial void LogActed(ILogger logger, int folder, int rule, HistoryOutcome outcome);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules folder {Folder}, rule {Rule} left a file where it is: {Reason}.")]
    private static partial void LogSkipped(ILogger logger, int folder, int rule, SkipReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused Folder Rules folder {Folder}, rule {Rule} (error {ErrorCode}).")]
    private static partial void LogRefused(ILogger logger, int folder, int rule, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Folder Rules folder {Folder} already handled a file that came back, so it's left as it is.")]
    private static partial void LogAlreadyHandled(ILogger logger, int folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules' history can't be read, so it starts empty and the file is replaced.")]
    private static partial void LogHistoryUnreadable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder Rules' history can't be saved (error {ErrorCode}).")]
    private static partial void LogHistoryNotSaved(ILogger logger, int errorCode);

    // A watched folder: its number in the settings, from 1, the rules that can be used, and its watch.
    private sealed class Folder(int number, IReadOnlyList<RuleMatcher> rules)
    {
        public int Number { get; set; } = number;

        public IReadOnlyList<RuleMatcher> Rules { get; set; } = rules;

        public IDisposable? Watch { get; set; }
    }

    // What runs on the thread: the watches, the files that wait to finish arriving, the history.
    private sealed class Running : IDisposable
    {
        private readonly FolderRulesThread _owner;
        private readonly MessageWindow _window;
        private readonly FolderRulesHistory _history;
        private readonly ArrivalWatch _arrivals;
        private readonly FolderRuleActions _actions;
        private readonly Dictionary<string, Folder> _folders = new(StringComparer.OrdinalIgnoreCase);
        private CoalescableTimer? _timer;
        private bool _applied;

        public Running(FolderRulesThread owner, MessageWindow window)
        {
            _owner = owner;
            _window = window;
            _history = owner._store.Load(out var readable);
            if (!readable)
            {
                LogHistoryUnreadable(owner._logger);
            }

            _arrivals = new ArrivalWatch(new Probe(owner._system));
            _actions = new FolderRuleActions(owner._system, owner._places, owner._time.LocalTimeZone);
        }

        private DateTimeOffset Now => _owner._time.GetUtcNow();

        public void Apply(FolderRulesSettings settings)
        {
            var wanted = settings.Enabled ? Usable(settings) : [];

            // A folder that's no longer watched starts over: when it's watched again, the files
            // already there are left alone.
            foreach (var (path, folder) in _folders.Where(pair => !wanted.ContainsKey(pair.Key)).ToArray())
            {
                folder.Watch?.Dispose();
                _arrivals.ForgetFolder(path);
                _folders.Remove(path);
                _history.ForgetFolder(path);
            }

            foreach (var (path, folder) in wanted)
            {
                if (_folders.TryGetValue(path, out var watched))
                {
                    watched.Number = folder.Number;
                    watched.Rules = folder.Rules;
                    continue;
                }

                _folders[path] = folder;

                // At start, files that arrived while PC Companion wasn't running are handled; a
                // folder that's turned on later starts from now.
                if (_applied)
                {
                    _history.ForgetFolder(path);
                }

                StartWatch(path, folder);
            }

            _applied = true;
            Save();
        }

        public void Dispose()
        {
            _timer?.Dispose();
            foreach (var folder in _folders.Values)
            {
                folder.Watch?.Dispose();
            }
        }

        // The enabled folders and rules that can be used, by the folder's full path.
        private Dictionary<string, Folder> Usable(FolderRulesSettings settings)
        {
            var logger = _owner._logger;
            var reviews = FolderRuleCheck.Review(settings, _owner._places);
            var usable = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
            var ruleCount = 0;
            for (var index = 0; index < settings.Folders.Count; index++)
            {
                var folder = settings.Folders[index];
                var review = reviews[index];
                ruleCount += folder.Rules.Count;
                if (review.Problem == FolderProblem.TooMany)
                {
                    continue;
                }

                if (review.Problem != FolderProblem.None)
                {
                    LogUnusableFolder(logger, index + 1, review.Problem);
                }

                var rules = new List<RuleMatcher>();
                for (var number = 1; number <= folder.Rules.Count; number++)
                {
                    var problem = review.Rules[number - 1];
                    if (problem is not (RuleProblem.None or RuleProblem.TooMany))
                    {
                        LogUnusableRule(logger, index + 1, number, problem);
                    }
                    else if (problem == RuleProblem.None && folder.Rules[number - 1].Enabled)
                    {
                        rules.Add(RuleMatcher.Create(folder.Rules[number - 1], number));
                    }
                }

                if (folder.Enabled && review.Problem == FolderProblem.None && FolderPaths.Normalize(folder.Path, out var path) == FolderProblem.None)
                {
                    usable[path] = new Folder(index + 1, rules);
                }
            }

            if (settings.Folders.Count > FolderRulesSettings.MaximumFolders)
            {
                LogTooManyFolders(logger, settings.Folders.Count, FolderRulesSettings.MaximumFolders);
            }

            if (reviews.Sum(review => review.Rules.Count(problem => problem == RuleProblem.TooMany)) > 0)
            {
                LogTooManyRules(logger, ruleCount, FolderRulesSettings.MaximumRules);
            }

            var usableRules = usable.Values.Sum(folder => folder.Rules.Count);
            LogApplied(logger, usable.Count, usableRules);
            return usable;
        }

        private void StartWatch(string path, Folder folder)
        {
            try
            {
                folder.Watch = _owner._system.Watch(
                    path,
                    file => _window.BeginInvoke(() => _owner.Guard(() => Changed(path, file))),
                    lostChanges => _window.BeginInvoke(() => _owner.Guard(() => WatchFailed(path, lostChanges))));
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException or Win32Exception)
            {
                LogNotWatched(_owner._logger, folder.Number, ErrorCode(exception));
                return;
            }

            Look(path, folder);
        }

        // Looks at the whole folder for files that arrived since the rules last looked at it.
        private void Look(string path, Folder folder)
        {
            var started = Now;
            var lastLooked = _history.LastLookedAt(path);
            IReadOnlyList<FileDetails> files;
            try
            {
                files = _owner._system.List(path);
            }
            catch (Win32Exception exception)
            {
                LogNotListed(_owner._logger, folder.Number, exception.NativeErrorCode);
                return;
            }

            var arrived = 0;
            foreach (var file in files.Where(file => !FolderRuleFilter.IsLeftAlone(file)))
            {
                if (FolderRulesHistory.ArrivedSince(file.Created, file.Changed, lastLooked))
                {
                    arrived++;
                    Notice(Path.Join(path, file.Name));
                }
            }

            LogLooked(_owner._logger, folder.Number, arrived, files.Count);
            _history.SetLastLooked(path, started);
            Save();
        }

        private void Changed(string path, string file)
        {
            // Only the files directly in a watched folder count.
            if (_folders.ContainsKey(path) && string.Equals(Path.GetDirectoryName(file), path, StringComparison.OrdinalIgnoreCase))
            {
                Notice(file);
            }
        }

        private void WatchFailed(string path, bool lostChanges)
        {
            if (!_folders.TryGetValue(path, out var folder))
            {
                return;
            }

            LogWatchFailed(_owner._logger, folder.Number, lostChanges);
            folder.Watch?.Dispose();
            folder.Watch = null;
            StartWatch(path, folder);
        }

        private void Notice(string file)
        {
            _arrivals.Notice(file, Now);
            if (_timer is null && _arrivals.Count > 0)
            {
                _timer = new CoalescableTimer(_window, CheckInterval, CheckInterval / 5);
                _timer.Elapsed += (_, _) => _owner.Guard(Check);
            }
        }

        private void Check()
        {
            foreach (var arrival in _arrivals.Check(Now))
            {
                Handle(arrival);
                if (_owner.IsStopping)
                {
                    return;
                }
            }

            if (_arrivals.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private void Handle(Arrival arrival)
        {
            var path = Path.GetDirectoryName(arrival.Path)!;
            if (!_folders.TryGetValue(path, out var folder) || ReadFile(arrival.Path) is not { } file || FolderRuleFilter.IsLeftAlone(file))
            {
                return;
            }

            if (_history.WasHandled(path, file.Name, new FileIdentity(file.FileId, file.Size, file.Modified)))
            {
                LogAlreadyHandled(_owner._logger, folder.Number);
                return;
            }

            var now = Now;
            var facts = new FileFacts(file.Name, file.Size, file.Created, file.Modified);
            if (RuleMatcher.FirstMatch(folder.Rules, facts, now, () => _owner._system.ReadMark(arrival.Path)) is not { } rule)
            {
                return;
            }

            var entry = arrival.Outcome == ArrivalOutcome.InUse
                ? new HistoryEntry
                {
                    Time = now,
                    Folder = path,
                    Rule = rule.Rule.Name.Trim(),
                    Name = file.Name,
                    Outcome = HistoryOutcome.Skipped,
                    Reason = SkipReason.InUse,
                    File = new FileIdentity(file.FileId, file.Size, file.Modified),
                }
                : _actions.Act(path, rule, file, now, _owner._stopping.Token);
            if (entry is null)
            {
                return;
            }

            _history.Add(entry);
            if (entry.Outcome == HistoryOutcome.Skipped)
            {
                LogSkipped(_owner._logger, folder.Number, rule.Number, entry.Reason);
            }
            else
            {
                LogActed(_owner._logger, folder.Number, rule.Number, entry.Outcome);
            }

            if (entry.Reason == SkipReason.Failed)
            {
                LogRefused(_owner._logger, folder.Number, rule.Number, entry.ErrorCode);
            }

            Save();
        }

        private FileDetails? ReadFile(string path)
        {
            try
            {
                return _owner._system.Read(path);
            }
            catch (Win32Exception)
            {
                return null;
            }
        }

        private void Save()
        {
            try
            {
                _owner._store.Save(_history);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogHistoryNotSaved(_owner._logger, exception.HResult);
            }
        }

        private static int ErrorCode(Exception exception) => exception is Win32Exception win32 ? win32.NativeErrorCode : exception.HResult;
    }

    // Reads files for the arrival watch: a file the rules leave alone, such as a link or a file kept
    // online only, isn't waited for, and isn't opened, which would download it.
    private sealed class Probe(IFolderRulesSystem system) : IArrivalProbe
    {
        public FileState? Read(string path)
        {
            try
            {
                return system.Read(path) is { } file && !FolderRuleFilter.IsLeftAlone(file) ? new FileState(file.Size, file.Modified) : null;
            }
            catch (Win32Exception)
            {
                return null;
            }
        }

        public bool CanOpenAlone(string path) => system.CanOpenAlone(path);
    }
}
