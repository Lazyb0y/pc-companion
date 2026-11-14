using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// Shows the files on the user's SFTP servers in File Explorer, through the Cloud Files API. The
/// files' work runs in a process of its own, <c>PCCompanion.RemoteFolders.exe</c>, which the host
/// starts while the feature is on, never with admin rights, and starts again when it ends
/// unexpectedly. The host asks it for the servers' states every 10 seconds, for its submenu and
/// its notifications. See docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class RemoteFoldersFeature : IFeature, IHostMenuItems
{
    /// <summary>How often the host asks the provider for the servers' states.</summary>
    public static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(10);

    // The hidden window through which notifications go to the host's UI thread.
    private const string WindowClassName = "PCCompanion.Host.RemoteFolders";

    private const int SyncNowItem = 1;
    private const int FirstOpenItem = 100;

    private static readonly TimeSpan FirstStatusAfter = TimeSpan.FromSeconds(2);

    private readonly IHostNotifications _notifications;
    private readonly IHostCommands _commands;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ProviderSupervisor _provider;
    private readonly Func<RemoteFoldersRequest, CancellationToken, Task<RemoteFoldersAnswer?>> _ask;
    private readonly Action<Action>? _postForTests;

    // Kept by the status timer's thread only.
    private readonly Dictionary<string, RemoteServerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _conflicts = new(StringComparer.OrdinalIgnoreCase);
    private int _asking;

    private volatile RemoteFoldersSettings? _applied;
    private volatile RemoteFoldersAnswer? _status;
    private MessageWindow? _window;
    private ITimer? _statusTimer;

    public RemoteFoldersFeature(IHostNotifications notifications, IHostCommands commands, TimeProvider time, ILogger<RemoteFoldersFeature> logger)
        : this(StartProvider, AskProviderToStop, RemoteFoldersPipe.AskAsync, null, notifications, commands, time, logger)
    {
    }

    /// <param name="startProvider">Starts the provider; tests give a fake.</param>
    /// <param name="askProviderToStop">Asks the running provider to stop.</param>
    /// <param name="ask">Asks the provider through its pipe.</param>
    /// <param name="postForTests">Runs work on the host's UI thread; <see langword="null"/> for the feature's own window.</param>
    public RemoteFoldersFeature(
        Func<RunningProcess> startProvider,
        Action askProviderToStop,
        Func<RemoteFoldersRequest, CancellationToken, Task<RemoteFoldersAnswer?>> ask,
        Action<Action>? postForTests,
        IHostNotifications notifications,
        IHostCommands commands,
        TimeProvider time,
        ILogger<RemoteFoldersFeature> logger)
    {
        _notifications = notifications;
        _commands = commands;
        _time = time;
        _logger = logger;
        _ask = ask;
        _postForTests = postForTests;
        _provider = new ProviderSupervisor(startProvider, askProviderToStop, time, logger);
        _provider.GaveUp += (_, _) => Post(NotifyStopped);
    }

    public static FeatureInfo Definition { get; } = new("remoteFolders", FilesModule.Id, "RemoteFoldersName", "RemoteFoldersDescription", "");

    /// <summary>Whether this PC can run Remote Folders, for the settings app, which doesn't create the feature.</summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    /// <summary>Whether the feature is on with settings it could read.</summary>
    public bool IsRunning => _applied is not null;

    /// <summary>The provider's process while it runs.</summary>
    public int? ProviderProcessId => _provider.ProcessId;

    public void Apply(JsonElement? settings)
    {
        RemoteFoldersSettings read;
        try
        {
            read = RemoteFoldersSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            TurnOff();
            return;
        }

        if (!read.Enabled)
        {
            TurnOff();
            return;
        }

        bool changed = read != _applied;
        if (changed)
        {
            LogApplied(_logger, read.Servers.Count, read.UsableServers.Count);
        }

        _applied = read;
        if (_postForTests is null)
        {
            _window ??= new MessageWindow(WindowClassName);
        }

        _statusTimer ??= _time.CreateTimer(_ => _ = AskStatusAsync(), null, FirstStatusAfter, StatusEvery);

        // The provider reads the settings itself; a change lets it start again after giving up.
        _provider.Run(settingsChanged: changed);
    }

    public IReadOnlyList<HostMenuItem> GetMenuItems()
    {
        if (_applied is not { } settings || settings.UsableServers.Count == 0)
        {
            return [];
        }

        var status = _status;
        var items = new List<HostMenuItem>();
        for (int index = 0; index < settings.UsableServers.Count; index++)
        {
            var server = settings.UsableServers[index];
            var state = status?.Servers.FirstOrDefault(found => found.Id.Equals(server.Id, StringComparison.OrdinalIgnoreCase));
            items.Add(HostMenuItem.Submenu(Format(Strings.RemoteFoldersMenuServer, server.Name, StateText(state)), [new HostMenuItem(FirstOpenItem + index, Strings.RemoteFoldersMenuOpenFolder)]));
        }

        items.Add(HostMenuItem.Separator);
        items.Add(new HostMenuItem(SyncNowItem, Strings.RemoteFoldersMenuSyncNow) { IsEnabled = status is not null });
        return [HostMenuItem.Submenu(Strings.RemoteFoldersName, items)];
    }

    public void ChooseMenuItem(int id)
    {
        if (id == SyncNowItem)
        {
            _ = Task.Run(() => _ask(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.SyncNow }, CancellationToken.None));
        }
        else if (_applied is { } settings && id - FirstOpenItem is var index && index >= 0 && index < settings.UsableServers.Count)
        {
            OpenFolder([settings.UsableServers[index].Folder]);
        }
    }

    public void Dispose()
    {
        _applied = null;
        _statusTimer?.Dispose();
        _statusTimer = null;
        _provider.Dispose();
        _window?.Dispose();
        _window = null;
    }

    /// <summary>A server's state for the menu, with the changes that wait for it.</summary>
    public static string StateText(RemoteServerStatus? status)
    {
        string state = status?.State switch
        {
            null => Strings.RemoteFoldersStateNotRunning,
            RemoteServerState.Connected => Strings.RemoteFoldersStateConnected,
            RemoteServerState.Unreachable => Strings.RemoteFoldersStateUnreachable,
            RemoteServerState.SignInFailed => Strings.RemoteFoldersStateSignInFailed,
            RemoteServerState.KeyChanged => Strings.RemoteFoldersStateKeyChanged,
            RemoteServerState.NoSafeAlgorithm => Strings.RemoteFoldersStateNoSafeAlgorithm,
            _ => Strings.RemoteFoldersStateNotConnected,
        };
        return status is { ChangesWaiting: > 0 } waiting
            ? Format(Strings.RemoteFoldersStateWithChanges, state, PluralStrings.Format("RemoteFoldersChangesWaiting", waiting.ChangesWaiting))
            : state;
    }

    /// <summary>Asks the provider for the servers' states, and shows what's new in notifications. Never throws.</summary>
    public async Task AskStatusAsync()
    {
        // One question at a time: a provider that's slow to answer doesn't pile them up.
        if (Interlocked.Exchange(ref _asking, 1) == 1)
        {
            return;
        }

        try
        {
            var status = await _ask(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.Status }, CancellationToken.None).ConfigureAwait(false);
            _status = status;
            if (status is not null && _applied is { } settings)
            {
                Compare(settings, status);
            }
        }
        catch (Exception exception)
        {
            LogStatusFailed(_logger, exception);
        }
        finally
        {
            Volatile.Write(ref _asking, 0);
        }
    }

    // Never with admin rights: a host elevated through UAC starts it with the shell's token.
    private static RunningProcess StartProvider()
    {
        string host = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        return Elevation.IsUacElevated
            ? UnelevatedProcess.StartKept(RemoteFoldersProcess.ExecutablePath, RemoteFoldersProcess.HostArgument + " " + host)
            : RunningProcess.Start(RemoteFoldersProcess.ExecutablePath, [RemoteFoldersProcess.HostArgument, host]);
    }

    private static void AskProviderToStop() => SessionSignal.Send(RemoteFoldersProcess.StopSignalName);

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.CurrentCulture, format, values);

    // A server that just failed to sign in or presented another key, or a new conflict copy, gets a
    // notification; only the last one shown can be selected, as for every feature's.
    private void Compare(RemoteFoldersSettings settings, RemoteFoldersAnswer status)
    {
        foreach (var server in status.Servers)
        {
            if (settings.Servers.FirstOrDefault(known => known.Id.Equals(server.Id, StringComparison.OrdinalIgnoreCase)) is not { } known)
            {
                continue;
            }

            _states.TryGetValue(server.Id, out var before);
            _states[server.Id] = server.State;
            if (server.State != before && server.State == RemoteServerState.SignInFailed)
            {
                Post(() => _notifications.Show(Format(Strings.RemoteFoldersSignInFailedTitle, known.Name), Strings.RemoteFoldersSignInFailedMessage, OpenPage));
            }
            else if (server.State != before && server.State == RemoteServerState.KeyChanged)
            {
                Post(() => _notifications.Show(Format(Strings.RemoteFoldersKeyChangedTitle, known.Name), Strings.RemoteFoldersKeyChangedMessage, OpenPage));
            }

            foreach (string copy in server.Conflicts)
            {
                string key = server.Id + "/" + copy;
                if (_conflicts.Add(key))
                {
                    string path = Path.Join(known.Folder, copy.Replace('/', '\\'));
                    Post(() => _notifications.Show(Strings.RemoteFoldersConflictTitle, Format(Strings.RemoteFoldersConflictMessage, known.Name, Path.GetFileName(path)), () => OpenFolder(["/select,", path])));
                }
            }
        }
    }

    private void OpenPage() => _commands.OpenFeatureSettings(Definition.Id);

    // Like a feature's program, never with admin rights; on the thread pool, since the host waits for Windows.
    private void OpenFolder(IReadOnlyList<string> arguments)
    {
        var explorer = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        _ = Task.Run(() =>
        {
            if (!_commands.StartProgram(explorer, arguments, out int error))
            {
                LogNotOpened(_logger, error);
            }
        });
    }

    private void Post(Action action)
    {
        if (_postForTests is { } post)
        {
            post(action);
        }
        else
        {
            _window?.BeginInvoke(action);
        }
    }

    private void TurnOff()
    {
        if (_applied is not null)
        {
            _provider.Stop();
        }

        _applied = null;
        _status = null;
        _statusTimer?.Dispose();
        _statusTimer = null;
    }

    private void NotifyStopped() =>
        _notifications.Show(Strings.RemoteFoldersStoppedTitle, Strings.RemoteFoldersStoppedMessage, OpenPage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Remote Folders settings can't be read, so Remote Folders is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remote Folders settings applied: {Servers} servers, {Usable} of them usable.")]
    private static partial void LogApplied(ILogger logger, int servers, int usable);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote Folders' states couldn't be read.")]
    private static partial void LogStatusFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Remote Folders folder couldn't be opened (error {Error}).")]
    private static partial void LogNotOpened(ILogger logger, int error);
}
